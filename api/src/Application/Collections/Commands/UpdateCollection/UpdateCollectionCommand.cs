using Application.Collections.Audit;
using Application.Collections.Exceptions;
using Application.Common.Features;
using Application.Common.Interfaces;
using Application.Common.Interfaces.Queries;
using Application.Common.Interfaces.Repositories;
using Application.Common.Permissions;
using Domain.AuditLogs;
using Domain.Collections;
using Domain.DataCollectionForms;
using Domain.Organisations;
using Domain.Users;
using LanguageExt;
using LanguageExt.UnsafeValueAccess;
using MediatR;
using DomainCollectionExceptions = Domain.Collections;

namespace Application.Collections.Commands.UpdateCollection;

public record UpdateCollectionCommand : IRequest<Either<CollectionException, Collection>>
{
    public Guid Id { get; init; }
    public string? Name { get; init; }
    public string? Purpose { get; init; }
    public string? Type { get; init; }
    public string? FinancialYear { get; init; }
    public int? Quarter { get; init; }
    public bool? WindowSameAsReportingPeriod { get; init; }
    public DateOnly? CollectionOpenDate { get; init; }
    public DateOnly? SubmissionDeadline { get; init; }
    public bool? IsSubmittedByAllLas { get; init; }
    public IReadOnlyList<Guid> FormModuleIds { get; init; } = [];
    public IReadOnlyList<Guid> LocalAuthorityIds { get; init; } = [];
    public IReadOnlyList<ReminderInput> Reminders { get; init; } = [];

    // When true the collection is (re-)saved as a draft; when false a draft collection is published on save.
    public bool SaveAsDraft { get; init; }
}

public class UpdateCollectionCommandHandler(
    ICollectionRepository collectionRepository,
    ICollectionQueries collectionQueries,
    IFormModuleVersionQueries formModuleVersionQueries,
    IFeatureFlags featureFlags,
    ISubmissionQueries submissionQueries,
    IAuditLogRepository auditLogRepository,
    ITransactionRunner transactionRunner,
    PermissionsService permissionsService,
    IDateTimeProvider dateTimeProvider)
    : IRequestHandler<UpdateCollectionCommand, Either<CollectionException, Collection>>
{
    public async Task<Either<CollectionException, Collection>> Handle(
        UpdateCollectionCommand request,
        CancellationToken cancellationToken)
    {
        return await permissionsService.GetUserId().MatchAsync<Either<CollectionException, Collection>>(
            actorId => HandleWithActor(request, actorId, cancellationToken),
            error => new CollectionForbiddenException(new CollectionId(request.Id), error.Message));
    }

    private async Task<Either<CollectionException, Collection>> HandleWithActor(
        UpdateCollectionCommand request,
        UserId actorId,
        CancellationToken cancellationToken)
    {
        var utcNow = dateTimeProvider.GetUtcNow();

        var collectionId = new CollectionId(request.Id);

        var lookup = await collectionQueries.GetByIdAsync(collectionId, includeDeleted: false, cancellationToken);
        if (lookup.IsNone)
        {
            return new CollectionDoesNotExistException(collectionId);
        }

        var collection = lookup.ValueUnsafe();

        var name = request.Name!.Trim();

        var nameTaken = await collectionQueries.ExistsByNameAsync(name, collectionId, cancellationToken);
        if (nameTaken)
        {
            return new CollectionNameTakenException(collectionId, name);
        }

        // BLUM-9106: with versioning on, both snapshots record the version each form uses, so the row shows a change.
        var recordVersions = featureFlags.IsEnabled(Feature.BLUM_9087);
        var before = CollectionSnapshot.Serialize(
            collection,
            utcNow,
            recordVersions ? await CollectionSnapshot.VersionNumbersAsync(collection, formModuleVersionQueries, cancellationToken) : null);

        var type = string.IsNullOrWhiteSpace(request.Type) ? null : CollectionType.From(request.Type);

        // A quarter is only meaningful for a quarterly report; with the type still unanswered (draft) the
        // quarter is kept as sent so nothing the user entered is dropped.
        var quarter = type is null || type.IsQuarterly ? request.Quarter : null;

        DateOnly? periodStart = null;
        DateOnly? periodEnd = null;
        if (!string.IsNullOrWhiteSpace(request.FinancialYear))
        {
            (periodStart, periodEnd) = ReportingPeriod.Derive(request.FinancialYear, quarter);
        }

        // Drafts persist exactly what was answered — unanswered questions stay null so the wizard resumes
        // them blank. Publishing is validated complete (window answer, dates, etc.) before this point.
        var (openDate, deadline) = request.WindowSameAsReportingPeriod == true
            ? (periodStart, periodEnd)
            : (request.CollectionOpenDate, request.SubmissionDeadline);

        var formModuleIds = request.FormModuleIds.Select(id => new FormModuleId(id)).ToList();
        var localAuthorityIds = request.LocalAuthorityIds.Select(id => new OrganisationId(id)).ToList();
        var reminders = request.Reminders
            .Select(r => (Offset: ReminderOffsetType.From(r.OffsetType), r.Days))
            .ToList();

        try
        {
            collection.UpdateDetails(
                name,
                request.Purpose,
                type,
                request.FinancialYear,
                quarter,
                periodStart,
                periodEnd,
                request.WindowSameAsReportingPeriod,
                openDate,
                deadline,
                request.IsSubmittedByAllLas,
                actorId,
                request.SaveAsDraft,
                utcNow);
        }
        catch (DomainCollectionExceptions.CollectionNotEditableException)
        {
            return new Application.Collections.Exceptions.CollectionNotEditableException(collectionId, collection.GetStatus(utcNow).ToString());
        }
        catch (DomainCollectionExceptions.CollectionInvalidWindowException exception)
        {
            return new Application.Collections.Exceptions.CollectionInvalidWindowException(collectionId, exception.Message);
        }
        catch (DomainCollectionExceptions.CollectionQuarterRequiredException)
        {
            return new Application.Collections.Exceptions.CollectionQuarterRequiredException(collectionId);
        }

        // The change and its audit row commit together (BLUM-8748).
        return await transactionRunner.RunAsync<CollectionException, Collection>(
            async token =>
            {
                // Once a local authority has started a report, its questions must not change under it (BLUM-9104 reads
                // them from the recorded versions), so neither the forms (BLUM-9084) nor a recorded version may change
                // then. With versioning on, the check runs under the collection's row lock, which a report start also
                // takes: a start that got there first is seen below, and one that comes later waits for this save and
                // reads what it leaves (BLUM-9084).
                if (recordVersions)
                {
                    await collectionRepository.LockForFormChangesAsync([collectionId], token);
                }

                bool? reportsStarted = null;
                async Task<bool> ReportsStartedAsync() =>
                    reportsStarted ??= await submissionQueries.AnyForCollectionAsync(collectionId, token);

                if (recordVersions
                    && !collection.Forms.Select(f => f.FormModuleId).ToHashSet().SetEquals(formModuleIds)
                    && await ReportsStartedAsync())
                {
                    return new CollectionFormsLockedException(collectionId);
                }

                collection.SetForms(formModuleIds);
                collection.SetLocalAuthorities(localAuthorityIds, utcNow);
                collection.SetReminders(reminders, utcNow);

                if (!request.SaveAsDraft && collection.IsDraft)
                {
                    collection.Publish(actorId, utcNow);
                }

                // BLUM-9103: a published collection records the latest version of any form that has none yet: every form
                // when it is first published, a newly added form when it is edited later. With versioning on, one whose
                // window has not opened also moves the forms it already records onto their latest version: a collection
                // reverted while open keeps its versions, and republished with a later open date it would otherwise wait
                // for the next form publish. (With it off, every report reads the live form and FormModuleVersionSync
                // moves every collection.)
                if (!collection.IsDraft)
                {
                    var latest = await formModuleVersionQueries.GetLatestVersionIdsAsync(formModuleIds, token);
                    collection.PinFormVersions(latest);

                    if (recordVersions && collection.FollowsLatestFormVersions(utcNow) && !await ReportsStartedAsync())
                    {
                        collection.FollowLatestFormVersions(latest);
                    }
                }

                try
                {
                    await collectionRepository.UpdateAsync(collection, token);

                    var versionNumbers = recordVersions
                        ? await CollectionSnapshot.VersionNumbersAsync(collection, formModuleVersionQueries, token)
                        : null;
                    await auditLogRepository.AppendAsync(
                        AuditLog.Create(
                            actorId,
                            AuditTargetType.Collection,
                            collection.Id.Value,
                            AuditAction.CollectionUpdated,
                            before: before,
                            after: CollectionSnapshot.Serialize(collection, utcNow, versionNumbers),
                            payloadVersion: recordVersions ? CollectionSnapshot.FormVersionsPayloadVersion : AuditLog.InitialPayloadVersion),
                        token);

                    return collection;
                }
                catch (Exception exception)
                {
                    return new CollectionUnknownException(collectionId, exception);
                }
            },
            cancellationToken);
    }
}