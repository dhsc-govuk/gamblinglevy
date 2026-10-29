using System.Text.Json;
using Application.Common.Csv;
using Application.Common.Funding;
using Application.Common.Interfaces;
using Application.Common.Interfaces.Repositories;
using Application.Common.Permissions;
using Application.Common.Validation;
using Application.Organisations.Commands.BulkImportLocalAuthorities;
using Application.Organisations.Exceptions;
using Domain.AuditLogs;
using Domain.Organisations;
using Domain.Users;
using LanguageExt;
using LanguageExt.UnsafeValueAccess;
using MediatR;
using Microsoft.AspNetCore.Http;

namespace Application.Organisations.Commands.BulkAmendLocalAuthorityAllocations;

public record BulkAmendLocalAuthorityAllocationsCommand : IRequest<Either<OrganisationException, BulkImportResult>>
{
    public IFormFile? File { get; init; }
}

public class BulkAmendLocalAuthorityAllocationsCommandHandler(
    IBulkUploadFileParser csvParser,
    IOrganisationRepository organisationRepository,
    IOrgFundingAllocationRepository allocationRepository,
    IAuditLogRepository auditLogRepository,
    PermissionsService permissionsService,
    IDateTimeProvider dateTimeProvider)
    : IRequestHandler<BulkAmendLocalAuthorityAllocationsCommand, Either<OrganisationException, BulkImportResult>>
{
    private static readonly string[] RequiredColumns =
    [
        "ons_code", "allocated_amount"
    ];

    private static readonly string[] ForbiddenColumns =
    [
        "name", "region", "type", "e_code",
        "q1_expected_spend", "q2_expected_spend", "q3_expected_spend", "q4_expected_spend"
    ];

    public async Task<Either<OrganisationException, BulkImportResult>> Handle(
        BulkAmendLocalAuthorityAllocationsCommand request,
        CancellationToken cancellationToken)
    {
        var actorResult = permissionsService.GetUserId();
        return await actorResult.MatchAsync<Either<OrganisationException, BulkImportResult>>(
            actorId => HandleWithActor(request, actorId, cancellationToken),
            e => new OrganisationUnknownException(OrganisationId.Empty(), e));
    }

    private async Task<Either<OrganisationException, BulkImportResult>> HandleWithActor(
        BulkAmendLocalAuthorityAllocationsCommand request,
        UserId actorId,
        CancellationToken cancellationToken)
    {
        if (request.File is null || !csvParser.CanParse(request.File))
        {
            return new OrganisationUnknownException(OrganisationId.Empty(), new ArgumentException("CSV file is required."));
        }

        var parseResult = await csvParser.ParseAsync(request.File, cancellationToken);
        if (!parseResult.IsSuccess)
        {
            return new OrganisationUnknownException(OrganisationId.Empty(), new ArgumentException(parseResult.ErrorMessage ?? "CSV parse failure"));
        }

        var rows = new List<BulkImportRowOutcome>();

        foreach (var record in parseResult.Records)
        {
            var rowNumber = int.TryParse(record.GetValueOrDefault("_RowNumber", "0"), out var n) ? n : 0;
            var onsCode = CsvParser.GetValue<string>(record, "ons_code");

            var forbidden = ForbiddenColumns.Where(record.ContainsKey).ToList();
            if (forbidden.Count > 0)
            {
                var msg = $"Unsupported columns for allocation amendments: {string.Join(", ", forbidden)}. Only ons_code and allocated_amount are accepted.";
                rows.Add(new BulkImportRowOutcome(rowNumber, onsCode, null, BulkImportOutcome.Failed, msg));
                continue;
            }

            try
            {
                CsvParser.ValidateRequiredColumns(record, RequiredColumns);
                CsvParser.ValidateRequiredValues(record, RequiredColumns);
            }
            catch (ArgumentException ex)
            {
                rows.Add(new BulkImportRowOutcome(rowNumber, onsCode, null, BulkImportOutcome.Failed, ex.Message));
                continue;
            }

            var allocatedAmount = CsvParser.GetValue<decimal?>(record, "allocated_amount") ?? 0m;
            if (allocatedAmount <= 0)
            {
                rows.Add(new BulkImportRowOutcome(rowNumber, onsCode, null, BulkImportOutcome.Failed, "allocated_amount must be greater than 0"));
                continue;
            }

            // The column is numeric(18,2), so a larger value raised 22003 inside the catch below — which
            // leaves the failed entity tracked and fails every subsequent row too (BLUM-7240).
            if (allocatedAmount > FieldLimits.MaxMoney)
            {
                var msg = $"allocated_amount must be {FieldLimits.MaxMoney:N0} or less";
                rows.Add(new BulkImportRowOutcome(rowNumber, onsCode, null, BulkImportOutcome.Failed, msg));
                continue;
            }

            try
            {
                var outcome = await AmendRowAsync(onsCode!, allocatedAmount, actorId, cancellationToken);
                rows.Add(new BulkImportRowOutcome(rowNumber, onsCode, outcome.Name, outcome.Outcome, outcome.Reason));
            }
            catch (Exception ex)
            {
                rows.Add(new BulkImportRowOutcome(rowNumber, onsCode, null, BulkImportOutcome.Failed, ex.Message));
            }
        }

        var result = new BulkImportResult(
            CreatedCount: rows.Count(r => r.Outcome == BulkImportOutcome.Created),
            UpdatedCount: rows.Count(r => r.Outcome == BulkImportOutcome.Updated),
            SkippedCount: rows.Count(r => r.Outcome == BulkImportOutcome.Skipped),
            FailedCount: rows.Count(r => r.Outcome == BulkImportOutcome.Failed),
            Rows: rows);

        await auditLogRepository.AppendAsync(
            AuditLog.Create(
                actorId,
                AuditTargetType.Organisation,
                Guid.Empty,
                AuditAction.OrganisationAllocationBulkAmended,
                before: null,
                after: JsonSerializer.Serialize(new
                {
                    result.CreatedCount,
                    result.UpdatedCount,
                    result.SkippedCount,
                    result.FailedCount,
                    rows = result.Rows,
                })),
            cancellationToken);

        return result;
    }

    private async Task<(BulkImportOutcome Outcome, string? Name, string? Reason)> AmendRowAsync(
        string onsCode,
        decimal allocatedAmount,
        UserId actorId,
        CancellationToken cancellationToken)
    {
        var utcNow = dateTimeProvider.GetUtcNow();

        var orgLookup = await organisationRepository.FindByOnsCodeAsync(onsCode, cancellationToken);
        if (orgLookup.IsNone)
        {
            return (BulkImportOutcome.Skipped, null, "Unrecognised ONS code");
        }

        var org = orgLookup.ValueUnsafe();

        // The CSV has one allocated_amount column and no financial year, so this file amends the year in
        // progress. AllocationSelector, not FindLatestForOrganisationAsync: the latter orders by CreatedAt and
        // therefore returns the UPCOMING year for any organisation holding two rows, so a bulk upload would have
        // silently rewritten next year's allocation and reported it as "Updated" (BLUM-7608).
        var held = await allocationRepository.ListWithWindowsForOrganisationsAsync([org.Id], cancellationToken);
        var (current, _) = AllocationSelector.Select(held, utcNow);
        if (current is null)
        {
            return (BulkImportOutcome.Failed, org.Name, "No existing allocation to amend");
        }

        var allocation = current.Allocation;
        allocation.UpdateAmount(allocatedAmount, actorId, utcNow);
        await allocationRepository.UpdateAsync(allocation, cancellationToken);

        return (BulkImportOutcome.Updated, org.Name, null);
    }
}