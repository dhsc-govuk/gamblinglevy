using Application.Collections.Audit;
using Application.Collections.Exceptions;
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
    IAuditLogRepository auditLogRepository,
    PermissionsService permissionsService)
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

        var before = CollectionSnapshot.Serialize(collection);

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
                request.SaveAsDraft);
        }
        catch (DomainCollectionExceptions.CollectionNotEditableException)
        {
            return new Application.Collections.Exceptions.CollectionNotEditableException(collectionId, collection.GetStatus().ToString());
        }
        catch (DomainCollectionExceptions.CollectionInvalidWindowException exception)
        {
            return new Application.Collections.Exceptions.CollectionInvalidWindowException(collectionId, exception.Message);
        }
        catch (DomainCollectionExceptions.CollectionQuarterRequiredException)
        {
            return new Application.Collections.Exceptions.CollectionQuarterRequiredException(collectionId);
        }

        collection.SetForms(formModuleIds);
        collection.SetLocalAuthorities(localAuthorityIds);
        collection.SetReminders(reminders);

        if (!request.SaveAsDraft && collection.IsDraft)
        {
            collection.Publish(actorId);
        }

        try
        {
            await collectionRepository.UpdateAsync(collection, cancellationToken);

            await auditLogRepository.AppendAsync(
                AuditLog.Create(
                    actorId,
                    AuditTargetType.Collection,
                    collection.Id.Value,
                    AuditAction.CollectionUpdated,
                    before: before,
                    after: CollectionSnapshot.Serialize(collection)),
                cancellationToken);

            return collection;
        }
        catch (Exception exception)
        {
            return new CollectionUnknownException(collectionId, exception);
        }
    }
}