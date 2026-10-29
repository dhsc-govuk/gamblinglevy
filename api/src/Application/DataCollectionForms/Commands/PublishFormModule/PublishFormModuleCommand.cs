using System.Text.Json;
using Application.Collections.Audit;
using Application.Common.Features;
using Application.Common.Interfaces;
using Application.Common.Interfaces.Queries;
using Application.Common.Interfaces.Repositories;
using Application.Common.Permissions;
using Application.DataCollectionForms.Audit;
using Application.DataCollectionForms.Exceptions;
using Application.DataCollectionForms.Versions;
using Domain.AuditLogs;
using Domain.Collections;
using Domain.DataCollectionForms;
using Domain.Users;
using LanguageExt;
using LanguageExt.UnsafeValueAccess;
using MediatR;

namespace Application.DataCollectionForms.Commands.PublishFormModule;

public record PublishFormModuleCommand : IRequest<Either<FormModuleException, PublishFormModuleResult>>
{
    public Guid Id { get; init; }
}

// LatestVersionNumber and VersionCreated are null/false while BLUM_9087 is off, when no version is written.
// VersionCreated is false with a LatestVersionNumber when the module was unchanged since that version (BLUM-9102).
public record PublishFormModuleResult(FormModule Module, int? LatestVersionNumber, bool VersionCreated);

public class PublishFormModuleCommandHandler(
    IFormModuleRepository moduleRepository,
    IFormModuleQueries moduleQueries,
    IFormModuleVersionRepository versionRepository,
    IFormModuleVersionQueries versionQueries,
    ICollectionQueries collectionQueries,
    ICollectionRepository collectionRepository,
    ISubmissionQueries submissionQueries,
    IAuditLogRepository auditLogRepository,
    ITransactionRunner transactionRunner,
    IFeatureFlags featureFlags,
    PermissionsService permissionsService,
    IDateTimeProvider dateTimeProvider)
    : IRequestHandler<PublishFormModuleCommand, Either<FormModuleException, PublishFormModuleResult>>
{
    public async Task<Either<FormModuleException, PublishFormModuleResult>> Handle(
        PublishFormModuleCommand request,
        CancellationToken cancellationToken)
    {
        return await permissionsService.GetUserId().MatchAsync<Either<FormModuleException, PublishFormModuleResult>>(
            actorId => HandleWithActor(request, actorId, cancellationToken),
            error => new FormModuleForbiddenException(FormModuleId.From(request.Id), error.Message));
    }

    private async Task<Either<FormModuleException, PublishFormModuleResult>> HandleWithActor(
        PublishFormModuleCommand request,
        UserId actorId,
        CancellationToken cancellationToken)
    {
        var moduleId = FormModuleId.From(request.Id);

        var lookup = await moduleQueries.GetByIdWithChildrenAsync(moduleId, cancellationToken);
        if (lookup.IsNone)
        {
            return new FormModuleNotFoundException(moduleId);
        }

        var module = lookup.ValueUnsafe();

        if (module.Status.IsPublished)
        {
            return new FormModuleNotEditableException(moduleId);
        }

        if (!module.Questions.Any(q => q.IsActive))
        {
            return new Application.DataCollectionForms.Exceptions.FormModuleEmptyOnPublishException(moduleId);
        }

        var versioning = featureFlags.IsEnabled(Feature.BLUM_9087);
        var latest = versioning
            ? await versionQueries.GetLatestAsync(moduleId, cancellationToken)
            : Option<FormModuleVersion>.None;

        // The publish, the new version, the collections it moves and their audit rows commit together (BLUM-8748). A
        // concurrent publish that took the same version number first fails the unique index, which rolls the whole
        // publish back.
        return await transactionRunner.RunAsync<FormModuleException, PublishFormModuleResult>(
            async token =>
            {
                try
                {
                    var utcNow = dateTimeProvider.GetUtcNow();
                    var before = DataCollectionFormSnapshot.Serialize(module);
                    module.Publish(actorId, utcNow);
                    await moduleRepository.UpdateAsync(module, token);

                    await auditLogRepository.AppendAsync(
                        AuditLog.Create(
                            actorId,
                            AuditTargetType.DataCollectionForm,
                            moduleId.Value,
                            AuditAction.DataCollectionFormModulePublished,
                            before,
                            DataCollectionFormSnapshot.Serialize(module)),
                        token);

                    if (!versioning)
                    {
                        return new PublishFormModuleResult(module, null, false);
                    }

                    var (definition, hash) = FormModuleDefinition.From(module);
                    if (latest.IsSome && latest.ValueUnsafe().ContentHash == hash)
                    {
                        // No new version, but a collection not yet open that is behind the latest still catches up.
                        await MoveCollectionsNotYetOpenAsync(module, latest.ValueUnsafe(), actorId, utcNow, token);
                        return new PublishFormModuleResult(module, latest.ValueUnsafe().VersionNumber, false);
                    }

                    var version = FormModuleVersion.Create(
                        FormModuleVersionId.New(),
                        moduleId,
                        latest.Match(v => v.VersionNumber, () => 0) + 1,
                        definition,
                        hash,
                        actorId,
                        utcNow);
                    await versionRepository.AddAsync(version, token);

                    await auditLogRepository.AppendAsync(
                        AuditLog.Create(
                            actorId,
                            AuditTargetType.DataCollectionForm,
                            moduleId.Value,
                            AuditAction.DataCollectionFormModuleVersionPublished,
                            null,
                            JsonSerializer.Serialize(new
                            {
                                moduleId = moduleId.Value,
                                code = module.Code,
                                versionId = version.Id.Value,
                                versionNumber = version.VersionNumber,
                                contentHash = version.ContentHash,
                            })),
                        token);

                    await MoveCollectionsNotYetOpenAsync(module, version, actorId, utcNow, token);

                    return new PublishFormModuleResult(module, version.VersionNumber, true);
                }
                catch (Domain.DataCollectionForms.FormModuleEmptyOnPublishException)
                {
                    return new Application.DataCollectionForms.Exceptions.FormModuleEmptyOnPublishException(moduleId);
                }
                catch (FormModuleAlreadyPublishedException)
                {
                    return new FormModuleNotEditableException(moduleId);
                }
                catch (FormModuleVersionNumberTakenException)
                {
                    return new FormModuleVersionConflictException(moduleId);
                }
                catch (Exception exception)
                {
                    return new FormModuleUnknownException(moduleId, exception);
                }
            },
            cancellationToken);
    }

    // BLUM-9103 (the PO's answer 1): a collection uses the latest published version of each form until its window
    // opens, so every collection that has not opened by this publish's instant moves onto the latest version — the one
    // this publish saved, or the existing one when the content was unchanged. One whose open date was moved back into
    // the future after a local authority started a report is left alone: a started report keeps the questions it
    // began with.
    private async Task MoveCollectionsNotYetOpenAsync(
        FormModule module,
        FormModuleVersion version,
        UserId actorId,
        DateTime utcNow,
        CancellationToken cancellationToken)
    {
        var latest = new Dictionary<FormModuleId, FormModuleVersionId> { [module.Id] = version.Id };
        var codes = new Dictionary<FormModuleId, string> { [module.Id] = module.Code };
        var collections = (await collectionQueries.GetNotYetOpenUsingFormAsync(module.Id, DateOnly.FromDateTime(utcNow), cancellationToken))
            .Where(c => c.FollowsLatestFormVersions(utcNow))
            .ToList();

        // Whether a report has started is checked under each collection's row lock, which a report start also takes
        // (BLUM-9084): a window opening as this publish runs cannot let a report begin on the version it is replacing.
        await collectionRepository.LockForFormChangesAsync(collections.Select(c => c.Id).ToList(), cancellationToken);

        var moved = new List<(CollectionId CollectionId, IReadOnlyList<FormVersionMove> Moves)>();
        foreach (var collection in collections)
        {
            if (await submissionQueries.AnyForCollectionAsync(collection.Id, cancellationToken))
            {
                continue;
            }

            var moves = collection.FollowLatestFormVersions(latest);
            if (moves.Count == 0)
            {
                continue;
            }

            await collectionRepository.UpdateAsync(collection, cancellationToken);
            moved.Add((collection.Id, moves));
        }

        if (moved.Count == 0)
        {
            return;
        }

        var numbers = await versionQueries.GetVersionNumbersAsync(
            moved.SelectMany(c => c.Moves).SelectMany(m => new[] { m.From, m.To }).Distinct().ToList(),
            cancellationToken);
        foreach (var (collectionId, moves) in moved)
        {
            await auditLogRepository.AppendAsync(
                CollectionFormVersionsFollowedAudit.Create(actorId, collectionId, moves, numbers, codes, CollectionFormVersionsFollowedAudit.PublishSource),
                cancellationToken);
        }
    }
}