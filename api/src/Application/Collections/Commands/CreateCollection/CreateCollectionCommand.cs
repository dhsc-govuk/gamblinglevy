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
using LanguageExt;
using MediatR;

namespace Application.Collections.Commands.CreateCollection;

public record CreateCollectionCommand : IRequest<Either<CollectionException, Collection>>
{
    public string Name { get; init; } = string.Empty;
    public string? Purpose { get; init; }
    public string? Type { get; init; }
    public string? FinancialYear { get; init; }
    public int? Quarter { get; init; }
    public bool? WindowSameAsReportingPeriod { get; init; }
    public DateOnly? OpenDate { get; init; }
    public DateOnly? Deadline { get; init; }
    public bool? IsSubmittedByAllLas { get; init; }
    public IReadOnlyList<Guid> LocalAuthorityIds { get; init; } = [];
    public IReadOnlyList<Guid> FormModuleIds { get; init; } = [];
    public IReadOnlyList<ReminderInput> Reminders { get; init; } = [];
    public bool SaveAsDraft { get; init; }
}

public class CreateCollectionCommandHandler(
    ICollectionRepository collectionRepository,
    ICollectionQueries collectionQueries,
    IFormModuleVersionQueries formModuleVersionQueries,
    IFeatureFlags featureFlags,
    IAuditLogRepository auditLogRepository,
    ITransactionRunner transactionRunner,
    PermissionsService permissionsService,
    IDateTimeProvider dateTimeProvider)
    : IRequestHandler<CreateCollectionCommand, Either<CollectionException, Collection>>
{
    public async Task<Either<CollectionException, Collection>> Handle(
        CreateCollectionCommand request,
        CancellationToken cancellationToken)
    {
        return await permissionsService.GetUserId().MatchAsync<Either<CollectionException, Collection>>(
            userId => HandleWithActor(request, userId, cancellationToken),
            error => new CollectionForbiddenException(CollectionId.Empty(), error.Message));
    }

    private async Task<Either<CollectionException, Collection>> HandleWithActor(
        CreateCollectionCommand request,
        Domain.Users.UserId userId,
        CancellationToken cancellationToken)
    {
        var collectionId = CollectionId.New();

        var nameTaken = await collectionQueries.ExistsByNameAsync(request.Name, null, cancellationToken);
        if (nameTaken)
        {
            return new CollectionNameTakenException(collectionId, request.Name);
        }

        // The change and its audit row commit together (BLUM-8748).
        return await transactionRunner.RunAsync<CollectionException, Collection>(
            async token =>
            {
                try
                {
                    var type = string.IsNullOrWhiteSpace(request.Type) ? null : CollectionType.From(request.Type);

                    // A quarter is only meaningful for a quarterly report; with the type still unanswered (draft) the
                    // quarter is kept as sent so nothing the user entered is dropped.
                    var quarter = type is null || type.IsQuarterly ? request.Quarter : null;

                    DateOnly? start = null;
                    DateOnly? end = null;
                    if (!string.IsNullOrWhiteSpace(request.FinancialYear))
                    {
                        (start, end) = ReportingPeriod.Derive(request.FinancialYear, quarter);
                    }

                    // Drafts persist exactly what was answered — unanswered questions stay null so the wizard resumes
                    // them blank. Publishing is validated complete (window answer, dates, etc.) before this point.
                    var (open, deadline) = request.WindowSameAsReportingPeriod == true
                        ? (start, end)
                        : (request.OpenDate, request.Deadline);

                    var utcNow = dateTimeProvider.GetUtcNow();
                    var collection = Collection.New(
                        collectionId,
                        request.Name,
                        request.Purpose,
                        type,
                        request.FinancialYear,
                        quarter,
                        start,
                        end,
                        request.WindowSameAsReportingPeriod,
                        open,
                        deadline,
                        request.IsSubmittedByAllLas,
                        userId,
                        request.SaveAsDraft,
                        utcNow);

                    var formModuleIds = request.FormModuleIds.Select(x => new FormModuleId(x)).ToList();
                    collection.SetForms(formModuleIds);

                    // BLUM-9103: a collection created published records the latest version of each form.
                    if (!collection.IsDraft)
                    {
                        collection.PinFormVersions(await formModuleVersionQueries.GetLatestVersionIdsAsync(formModuleIds, token));
                    }

                    collection.SetLocalAuthorities(request.LocalAuthorityIds.Select(x => new OrganisationId(x)).ToList(), utcNow);
                    collection.SetReminders(
                        request.Reminders.Select(r => (ReminderOffsetType.From(r.OffsetType), r.Days)).ToList(), utcNow);

                    var saved = await collectionRepository.AddAsync(collection, token);

                    // BLUM-9106: with versioning on, the row records the version each form took.
                    var versionNumbers = featureFlags.IsEnabled(Feature.BLUM_9087)
                        ? await CollectionSnapshot.VersionNumbersAsync(saved, formModuleVersionQueries, token)
                        : null;
                    await auditLogRepository.AppendAsync(
                        AuditLog.Create(
                            userId,
                            AuditTargetType.Collection,
                            saved.Id.Value,
                            AuditAction.CollectionCreated,
                            before: null,
                            after: CollectionSnapshot.Serialize(saved, utcNow, versionNumbers),
                            payloadVersion: versionNumbers is null ? AuditLog.InitialPayloadVersion : CollectionSnapshot.FormVersionsPayloadVersion),
                        token);

                    return saved;
                }
                catch (Domain.Collections.CollectionInvalidWindowException exception)
                {
                    return new Exceptions.CollectionInvalidWindowException(collectionId, exception.Message);
                }
                catch (Domain.Collections.CollectionQuarterRequiredException)
                {
                    return new Exceptions.CollectionQuarterRequiredException(collectionId);
                }
                catch (Exception exception)
                {
                    return new CollectionUnknownException(collectionId, exception);
                }
            },
            cancellationToken);
    }
}