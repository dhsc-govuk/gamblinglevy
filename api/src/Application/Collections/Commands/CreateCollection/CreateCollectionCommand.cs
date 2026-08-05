using Application.Collections.Audit;
using Application.Collections.Exceptions;
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
    IAuditLogRepository auditLogRepository,
    PermissionsService permissionsService)
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
                request.SaveAsDraft);

            collection.SetForms(request.FormModuleIds.Select(x => new FormModuleId(x)).ToList());
            collection.SetLocalAuthorities(request.LocalAuthorityIds.Select(x => new OrganisationId(x)).ToList());
            collection.SetReminders(
                request.Reminders.Select(r => (ReminderOffsetType.From(r.OffsetType), r.Days)).ToList());

            var saved = await collectionRepository.AddAsync(collection, cancellationToken);

            await auditLogRepository.AppendAsync(
                AuditLog.Create(
                    userId,
                    AuditTargetType.Collection,
                    saved.Id.Value,
                    AuditAction.CollectionCreated,
                    before: null,
                    after: CollectionSnapshot.Serialize(saved)),
                cancellationToken);

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
    }
}