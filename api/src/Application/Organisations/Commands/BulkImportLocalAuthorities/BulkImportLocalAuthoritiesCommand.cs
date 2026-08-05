using System.Text.Json;
using Application.Common.Csv;
using Application.Common.Funding;
using Application.Common.Interfaces;
using Application.Common.Interfaces.Repositories;
using Application.Common.Permissions;
using Application.Common.Validation;
using Application.Organisations.Exceptions;
using Domain.AuditLogs;
using Domain.Funding;
using Domain.Organisations;
using Domain.Users;
using LanguageExt;
using LanguageExt.UnsafeValueAccess;
using MediatR;
using Microsoft.AspNetCore.Http;

namespace Application.Organisations.Commands.BulkImportLocalAuthorities;

public record BulkImportLocalAuthoritiesCommand : IRequest<Either<OrganisationException, BulkImportResult>>
{
    // The importer's column contract, in the order the downloadable template lists them. Both sides read from
    // here: the template endpoint builds its header row from this array, and the handler derives RequiredColumns
    // from it. They used to be two hand-maintained literals that this feature had to edit in lockstep, so the
    // next column change that updated only one of them would have shipped an official template the importer
    // itself rejects (BLUM-7050 review).
    public static readonly string[] TemplateColumns =
    [
        "name", "ons_code", "region", "type", "e_code", "allocated_amount"
    ];

    // The one template column that is not required for every row: an ONS code identifies a Local Authority
    // geography, so a VCSE row legitimately has none (BLUM-7051).
    public const string OnsCodeColumn = "ons_code";

    public IFormFile? File { get; init; }
}

public enum BulkImportOutcome { Created, Updated, Skipped, Failed }

public record BulkImportRowOutcome(int RowNumber, string? OnsCode, string? Name, BulkImportOutcome Outcome, string? Reason = null);

public record BulkImportResult(
    int CreatedCount,
    int UpdatedCount,
    int SkippedCount,
    int FailedCount,
    IReadOnlyList<BulkImportRowOutcome> Rows);

public class BulkImportLocalAuthoritiesCommandHandler(
    IBulkUploadFileParser csvParser,
    IOrganisationRepository organisationRepository,
    IOrgFundingAllocationRepository allocationRepository,
    IFundingWindowResolver fundingWindowResolver,
    IAuditLogRepository auditLogRepository,
    PermissionsService permissionsService)
    : IRequestHandler<BulkImportLocalAuthoritiesCommand, Either<OrganisationException, BulkImportResult>>
{
    // Every template column except ons_code, which is required for Local Authority rows only — that cannot be
    // known until the row's type has been resolved, so it is enforced per row further down rather than as a
    // blanket column rule. Deriving rather than restating the list means a column added to the template is
    // required by default; excluding it has to be a deliberate edit here.
    //
    // e_code is required for every organisation type (BLUM-7050), so it stays in the derived set: a missing
    // column is then reported once against the header instead of once per data row.
    private static readonly string[] RequiredColumns =
        [.. BulkImportLocalAuthoritiesCommand.TemplateColumns.Where(c => c != BulkImportLocalAuthoritiesCommand.OnsCodeColumn)];

    private static readonly string[] ForbiddenQuarterlyColumns =
    [
        "q1_expected_spend", "q2_expected_spend", "q3_expected_spend", "q4_expected_spend"
    ];

    private const string FileMissingCode = "BULK_IMPORT_FILE_MISSING";
    private const string FileMissingMessage = "Select a CSV file to upload.";

    private const string FileEmptyCode = "BULK_IMPORT_FILE_EMPTY";
    private const string FileEmptyMessage =
        "The selected file is empty. Download the template, add one row for each Local Authority, and upload it again.";

    private const string FileNotCsvCode = "BULK_IMPORT_FILE_NOT_CSV";
    private const string FileNotCsvMessage =
        "The selected file must be a CSV file (.csv). Download the template, add your rows, and save the file as CSV before uploading.";

    private const string FileInvalidCode = "BULK_IMPORT_FILE_INVALID";

    // Covers the header-row case explicitly: with the missing/empty/not-csv cases guarded above, a parse
    // failure here is in practice a missing or unreadable header row.
    private const string FileInvalidMessage =
        "The CSV file could not be read. Check it has a header row matching the template, then upload it again.";

    private const string NoDataRowsCode = "BULK_IMPORT_NO_DATA_ROWS";
    private const string NoDataRowsMessage =
        "The CSV file contains no Local Authority rows to import. Add at least one row below the header row and upload the file again.";

    public async Task<Either<OrganisationException, BulkImportResult>> Handle(
        BulkImportLocalAuthoritiesCommand request,
        CancellationToken cancellationToken)
    {
        var actorResult = permissionsService.GetUserId();
        return await actorResult.MatchAsync<Either<OrganisationException, BulkImportResult>>(
            actorId => HandleWithActor(request, actorId, cancellationToken),
            e => new OrganisationUnknownException(OrganisationId.Empty(), e));
    }

    private async Task<Either<OrganisationException, BulkImportResult>> HandleWithActor(
        BulkImportLocalAuthoritiesCommand request,
        UserId actorId,
        CancellationToken cancellationToken)
    {
        if (request.File is null)
        {
            return Failed(FileMissingCode, FileMissingMessage);
        }

        // Extension is checked before emptiness: a 0-byte .xlsx is a wrong-format problem, and telling the
        // user it is empty sends them off to add rows and re-upload the same unusable format.
        if (!Path.GetExtension(request.File.FileName).Equals(".csv", StringComparison.OrdinalIgnoreCase))
        {
            return Failed(FileNotCsvCode, FileNotCsvMessage);
        }

        if (request.File.Length == 0 || !csvParser.CanParse(request.File))
        {
            return Failed(FileEmptyCode, FileEmptyMessage);
        }

        var parseResult = await csvParser.ParseAsync(request.File, cancellationToken);
        if (!parseResult.IsSuccess)
        {
            // The parser only sets ErrorMessage for its own author-written validation failures (missing
            // header row, duplicate columns); it returns null for unexpected exceptions rather than leak
            // their text, so this fallback is the user-facing copy for that case.
            return Failed(FileInvalidCode, parseResult.ErrorMessage ?? FileInvalidMessage);
        }

        // Must stay above ResolveCurrentAsync: that call persists a FundingWindow when none exists for the
        // financial year, so rejecting below it would leave a DB row behind as a side effect of a 400.
        // Returning here also skips the audit append — a rejected upload must not be audited as an import.
        if (parseResult.Records.Count == 0)
        {
            return Failed(NoDataRowsCode, NoDataRowsMessage);
        }

        // Column presence is a property of the header, not of any one row, so it is checked once here rather
        // than inside the loop. Checking per row turned a single missing column into one identical failure per
        // data row — a 153-authority file produced 153 copies of the same message instead of one clear error.
        try
        {
            CsvParser.ValidateRequiredColumns(parseResult.Records[0], RequiredColumns);
        }
        catch (ArgumentException ex)
        {
            return Failed(FileInvalidCode, ex.Message);
        }

        var window = await fundingWindowResolver.ResolveCurrentAsync(cancellationToken);
        var rows = new List<BulkImportRowOutcome>();

        foreach (var record in parseResult.Records)
        {
            var rowNumber = int.TryParse(record.GetValueOrDefault("_RowNumber", "0"), out var n) ? n : 0;
            var onsCode = CsvParser.GetValue<string>(record, BulkImportLocalAuthoritiesCommand.OnsCodeColumn);
            var name = CsvParser.GetValue<string>(record, "name");

            var forbidden = ForbiddenQuarterlyColumns.Where(record.ContainsKey).ToList();
            if (forbidden.Count > 0)
            {
                var msg = $"Quarterly profile columns are not supported and must be removed: {string.Join(", ", forbidden)}";
                rows.Add(new BulkImportRowOutcome(rowNumber, onsCode, name, BulkImportOutcome.Failed, msg));
                continue;
            }

            try
            {
                CsvParser.ValidateRequiredValues(record, RequiredColumns);
            }
            catch (ArgumentException ex)
            {
                rows.Add(new BulkImportRowOutcome(rowNumber, onsCode, name, BulkImportOutcome.Failed, ex.Message));
                continue;
            }

            var region = CsvParser.GetValue<string>(record, "region");
            var type = CsvParser.GetValue<string>(record, "type");
            var eCode = CsvParser.GetValue<string>(record, "e_code");
            var allocatedAmount = CsvParser.GetValue<decimal?>(record, "allocated_amount") ?? 0m;

            if (allocatedAmount <= 0)
            {
                rows.Add(new BulkImportRowOutcome(rowNumber, onsCode, name, BulkImportOutcome.Failed, "allocated_amount must be greater than 0"));
                continue;
            }

            // The same class of bug as the e_code rule below, for the three other cells that reach a bounded
            // column: name is varchar(255), ons_code varchar(64), and allocated_amount numeric(18,2). Each was
            // caught only by the generic catch at the end of the loop, which is exactly the poisoned-context
            // path that comment describes (BLUM-7240).
            if (allocatedAmount > FieldLimits.MaxMoney)
            {
                var msg = $"allocated_amount must be {FieldLimits.MaxMoney:N0} or less";
                rows.Add(new BulkImportRowOutcome(rowNumber, onsCode, name, BulkImportOutcome.Failed, msg));
                continue;
            }

            if (name!.Trim().Length > FieldLimits.Name)
            {
                var msg = $"name must be {FieldLimits.Name} characters or fewer";
                rows.Add(new BulkImportRowOutcome(rowNumber, onsCode, name, BulkImportOutcome.Failed, msg));
                continue;
            }

            if (onsCode is not null && onsCode.Trim().Length > FieldLimits.OnsCode)
            {
                var msg = $"ons_code must be {FieldLimits.OnsCode} characters or fewer";
                rows.Add(new BulkImportRowOutcome(rowNumber, onsCode, name, BulkImportOutcome.Failed, msg));
                continue;
            }

            // Mirrors the length rule both command validators apply. This is not cosmetic parity: without it an
            // over-long cell reaches the varchar(64) column, SaveChanges throws 22001, and RepositoryBase rethrows
            // without detaching — so the entity stays tracked in Added state on the scoped DbContext and every
            // later row's SaveChanges retries that same failing insert. One bad cell used to fail the whole rest
            // of the file, each row reporting a raw PostgreSQL error (BLUM-7050 review).
            if (eCode!.Trim().Length > Organisation.ECodeMaxLength)
            {
                var msg = $"e_code must be {Organisation.ECodeMaxLength} characters or fewer";
                rows.Add(new BulkImportRowOutcome(rowNumber, onsCode, name, BulkImportOutcome.Failed, msg));
                continue;
            }

            var canonicalRegion = ResolveRegion(region!);
            if (canonicalRegion is null)
            {
                var msg = $"region '{region}' is not recognised. Use one of: {string.Join(", ", Region.SupportedRegions)}";
                rows.Add(new BulkImportRowOutcome(rowNumber, onsCode, name, BulkImportOutcome.Failed, msg));
                continue;
            }

            var canonicalType = ResolveOrgType(type!);
            if (canonicalType is null)
            {
                var msg = $"type '{type}' is not recognised. Use {OrgTypes.LocalAuthority} (or Local Authority) for a local authority, or {OrgTypes.Vcse}.";
                rows.Add(new BulkImportRowOutcome(rowNumber, onsCode, name, BulkImportOutcome.Failed, msg));
                continue;
            }

            // Enforced here rather than via RequiredColumns because it depends on the row's resolved type:
            // an ONS code identifies a Local Authority geography, so a VCSE row correctly has none.
            if (canonicalType == OrgTypes.LocalAuthority && string.IsNullOrWhiteSpace(onsCode))
            {
                rows.Add(new BulkImportRowOutcome(rowNumber, onsCode, name, BulkImportOutcome.Failed, "ons_code is required for Local Authority rows"));
                continue;
            }

            var regionVo = Region.From(canonicalRegion);
            var typeVo = OrgType.From(canonicalType);

            try
            {
                var (outcome, reason) = await UpsertRowAsync(name!, onsCode, regionVo, typeVo, eCode, allocatedAmount, window, actorId, cancellationToken);
                rows.Add(new BulkImportRowOutcome(rowNumber, onsCode, name, outcome, reason));
            }
            catch (Exception ex)
            {
                rows.Add(new BulkImportRowOutcome(rowNumber, onsCode, name, BulkImportOutcome.Failed, ex.Message));
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
                AuditAction.OrganisationBulkImported,
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

    private static OrganisationBulkImportFailedException Failed(string code, string message) =>
        new(OrganisationId.Empty(), code, message);

    // A CSV is the only free-text way into these values — every other entry point is a dropdown — so an
    // admin types whatever the UI showed them. The create form's radio for the "LA" value is *labelled*
    // "Local Authority", which is why that label has to resolve here. Kept to the labels the UI actually
    // renders rather than guessing at synonyms.
    private static readonly Dictionary<string, string> OrgTypeAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        [OrgTypes.LocalAuthority] = OrgTypes.LocalAuthority,
        ["Local Authority"] = OrgTypes.LocalAuthority,
        [OrgTypes.Vcse] = OrgTypes.Vcse,
    };

    // Resolves to the canonical value rather than the user's spelling: the value objects persist via
    // HasConversion(x => x.ToString()), so returning the raw input would write mixed casing to the database.
    private static string? ResolveOrgType(string value) =>
        OrgTypeAliases.GetValueOrDefault(value.Trim());

    private static string? ResolveRegion(string value) =>
        Region.SupportedRegions
            .FirstOrDefault(r => string.Equals(r.ToString(), value.Trim(), StringComparison.OrdinalIgnoreCase))
            ?.ToString();

    private async Task<(BulkImportOutcome Outcome, string? Reason)> UpsertRowAsync(
        string name,
        string? onsCode,
        Region region,
        OrgType type,
        string? eCode,
        decimal allocatedAmount,
        FundingWindow window,
        UserId actorId,
        CancellationToken cancellationToken)
    {
        var trimmedName = name.Trim();
        var trimmedOnsCode = string.IsNullOrWhiteSpace(onsCode) ? null : onsCode.Trim();
        // Non-null by the time a row reaches here: e_code is in RequiredColumns, so ValidateRequiredValues
        // fails a row with a blank cell before this is called.
        var trimmedECode = eCode!.Trim();

        // The ONS code is the match key: a row whose code is already on file updates that organisation in
        // place, which is what makes re-uploading the same file idempotent. A VCSE row has no code and so no
        // key, and falls through to the create path below.
        if (trimmedOnsCode is not null)
        {
            var existing = await organisationRepository.FindByOnsCodeAsync(trimmedOnsCode, cancellationToken);

            if (existing.IsSome)
            {
                var org = existing.ValueUnsafe();

                // Guards the rename an ONS-code match would otherwise wave through. organisations.name has no
                // unique index, so a row pairing one organisation's name with another's code silently renamed
                // the matched organisation onto a name already in use, leaving two identical names that the
                // edit form then refuses to save. Mirrors UpdateLocalAuthorityCommand's own duplicate check.
                if (!string.Equals(org.Name, trimmedName, StringComparison.Ordinal))
                {
                    var clash = await organisationRepository.FindDuplicateAsync(trimmedName, org.Id, cancellationToken);
                    if (clash.IsSome)
                    {
                        return (BulkImportOutcome.Failed, $"An organisation named '{trimmedName}' already exists, so '{org.Name}' cannot be renamed to it.");
                    }
                }

                org.UpdateDetails(trimmedName, region, type, trimmedECode, trimmedOnsCode);
                await organisationRepository.UpdateAsync(org, cancellationToken);

                var allocation = await allocationRepository.FindForOrganisationAndWindowAsync(org.Id, window.Id, cancellationToken);
                if (allocation.IsSome)
                {
                    var a = allocation.ValueUnsafe();
                    a.UpdateAmount(allocatedAmount, actorId);
                    await allocationRepository.UpdateAsync(a, cancellationToken);
                }
                else
                {
                    await allocationRepository.AddAsync(
                        OrgFundingAllocation.New(OrgFundingAllocationId.New(), org.Id, window.Id, allocatedAmount, actorId),
                        cancellationToken);
                }

                return (BulkImportOutcome.Updated, null);
            }
        }

        // Mirrors CreateLocalAuthorityCommand, which rejects a duplicate name outright. Reached when the row's
        // ONS code is not on file yet (or it is a VCSE row with none) but reuses an existing organisation's
        // name — updating that organisation requires its own code, not a second row.
        var duplicateName = await organisationRepository.FindByNameAsync(trimmedName, cancellationToken);
        if (duplicateName.IsSome)
        {
            return (BulkImportOutcome.Failed, $"An organisation named '{trimmedName}' already exists. Use its existing ONS code to update it instead.");
        }

        var newOrg = Organisation.New(
            OrganisationId.New(),
            trimmedName,
            trimmedOnsCode,
            region,
            type,
            trimmedECode,
            OnboardingStatus.Invited);
        var savedOrg = await organisationRepository.AddAsync(newOrg, cancellationToken);

        await allocationRepository.AddAsync(
            OrgFundingAllocation.New(OrgFundingAllocationId.New(), savedOrg.Id, window.Id, allocatedAmount, actorId),
            cancellationToken);

        return (BulkImportOutcome.Created, null);
    }
}