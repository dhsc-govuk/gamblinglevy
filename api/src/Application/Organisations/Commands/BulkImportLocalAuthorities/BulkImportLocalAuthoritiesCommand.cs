using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using Application.Common.Csv;
using Application.Common.Funding;
using Application.Common.Interfaces;
using Application.Common.Interfaces.Repositories;
using Application.Common.Permissions;
using Application.Common.Settings;
using Application.Common.Validation;
using Application.Organisations.Exceptions;
using Application.Users.Commands.OrganisationUsers;
using Application.Users.Exceptions;
using Domain.AuditLogs;
using Domain.Funding;
using Domain.Organisations;
using Domain.Users;
using Domain.ValueObjects;
using LanguageExt;
using LanguageExt.UnsafeValueAccess;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

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
        "name", "ons_code", "region", "type", "e_code", "allocated_amount",
        "la_admin_first_name", "la_admin_last_name", "la_admin_access_level", "la_admin_email"
    ];

    // An ONS code identifies a Local Authority geography, so a VCSE row legitimately has none (BLUM-7051).
    public const string OnsCodeColumn = "ons_code";

    // The first LA Admin for the organisation, created from the same row (BLUM-7609).
    public const string AdminFirstNameColumn = "la_admin_first_name";
    public const string AdminLastNameColumn = "la_admin_last_name";
    public const string AdminAccessLevelColumn = "la_admin_access_level";
    public const string AdminEmailColumn = "la_admin_email";

    // Template columns that RequiredColumns must not demand of every row. ons_code is conditional on the
    // row's resolved type and is enforced per row further down. The four la_admin_* columns are optional
    // outright: an organisation may be onboarded before its first admin is known, and a file written
    // against the pre-BLUM-7609 template must keep importing unchanged.
    public static readonly string[] OptionalColumns =
    [
        OnsCodeColumn, AdminFirstNameColumn, AdminLastNameColumn, AdminAccessLevelColumn, AdminEmailColumn
    ];

    public IFormFile? File { get; init; }
}

public enum BulkImportOutcome { Created, Updated, Skipped, Failed }

/// <summary>
/// The fate of the row's LA Admin, reported alongside — never instead of — the organisation's own outcome.
/// The importer is not transactional, so a row whose organisation was written and whose user was not really
/// did half of what it said; collapsing that into <see cref="BulkImportOutcome.Failed"/> would report an
/// organisation as absent while it sits in the database, and would desync FailedCount from what an admin
/// sees in the Local Authorities list.
/// </summary>
public enum BulkImportAdminUserOutcome
{
    NotProvided,
    NotAttempted,
    Created,
    AlreadyExists,
    Failed,
}

// The trailing parameters carry defaults because BulkAmendLocalAuthorityAllocationsCommandHandler
// constructs this same record and has no admin-user concept.
public record BulkImportRowOutcome(
    int RowNumber,
    string? OnsCode,
    string? Name,
    BulkImportOutcome Outcome,
    string? Reason = null,
    BulkImportAdminUserOutcome AdminUserOutcome = BulkImportAdminUserOutcome.NotProvided,
    string? AdminUserEmail = null,
    string? AdminUserReason = null);

public record BulkImportResult(
    int CreatedCount,
    int UpdatedCount,
    int SkippedCount,
    int FailedCount,
    IReadOnlyList<BulkImportRowOutcome> Rows,
    int AdminUsersCreatedCount = 0,
    int AdminUsersFailedCount = 0,
    int AdminUsersNotAttemptedCount = 0);

public class BulkImportLocalAuthoritiesCommandHandler(
    IBulkUploadFileParser csvParser,
    IOrganisationRepository organisationRepository,
    IOrgFundingAllocationRepository allocationRepository,
    IFundingWindowResolver fundingWindowResolver,
    IAuditLogRepository auditLogRepository,
    IOrganisationUserRepository organisationUserRepository,
    PermissionsService permissionsService,
    ISender sender,
    IServiceScopeFactory scopeFactory,
    IDateTimeProvider dateTimeProvider,
    ApplicationSettings settings)
    : IRequestHandler<BulkImportLocalAuthoritiesCommand, Either<OrganisationException, BulkImportResult>>
{
    // Every template column except the ones the command lists as optional. Deriving rather than restating
    // the list means a column added to the template is required by default; excluding it has to be a
    // deliberate edit to OptionalColumns.
    //
    // e_code is required for every organisation type (BLUM-7050), so it stays in the derived set: a missing
    // column is then reported once against the header instead of once per data row.
    private static readonly string[] RequiredColumns =
        [.. BulkImportLocalAuthoritiesCommand.TemplateColumns.Except(BulkImportLocalAuthoritiesCommand.OptionalColumns)];

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

    // The two access levels an LA user can hold, spelled as the UI spells them. ROLE_LABELS in the client
    // renders Role.OrganisationAdmin as "LA Admin" and Role.Lead as "LA Lead", and a CSV is the only
    // free-text way into this value — an admin types whatever the screen showed them. The raw wire strings
    // are accepted too so a file exported from the API round-trips. AccessLevelAliases, below, is what reads
    // these; the bulk-import screen's own guidance copy names the same two labels.
    private const string LaAdminLabel = "LA Admin";
    private const string LaLeadLabel = "LA Lead";

    // "user", not "LA Admin": la_admin_access_level also accepts LA Lead, so a row that asked for a Lead must
    // not be told its LA Admin was not created.
    private const string AdminNotAttemptedReason = "The organisation row did not import, so no user was created for it.";

    private const string AdminCreationFailedReason = "The user could not be created. Try this row again, and report it if it keeps failing.";

    // Two spellings because the recovery genuinely differs. A row carrying an ONS code is matched on it, so
    // re-uploading updates the organisation in place and provisions the user. A row without one — a VCSE row,
    // which legitimately has no code — cannot be re-imported at all: UpsertRowAsync falls through to the create
    // path, FindByNameAsync hits the organisation the first upload wrote, and the row fails as a duplicate
    // name. Telling that admin to upload the file again sends them somewhere the importer will refuse them and
    // leaves the user unprovisioned with no way back.
    private const string AdminBudgetExhaustedReason =
        "The import ran out of time before reaching this user. The organisation was imported — upload the file again to finish the remaining users.";

    private const string AdminBudgetExhaustedNoOnsCodeReason =
        "The import ran out of time before reaching this user. The organisation was imported — add this user from its Users page. "
        + "Re-uploading will not work for a row with no ons_code, because the organisation already exists under that name.";

    private const string AdminTimedOutReason =
        "Creating this user took too long and was abandoned so the rest of the import could finish. Upload the file again to retry it.";

    // Guard rails on the two configured values. Both come from an App Service app setting a human types, and
    // neither a 0 nor a 500 may be allowed to break an import: 0 workers would hang, and a budget of 0 would
    // provision nobody while still reporting success. Same reasoning as
    // MfaSessionRetentionHostedService.MaxIntervalHours.
    private const int MinWorkers = 1;
    private const int MaxWorkers = 16;
    private const int MinBudgetSeconds = 5;
    private const int MaxBudgetSeconds = 600;
    private const int MinUserTimeoutSeconds = 5;
    private const int MaxUserTimeoutSeconds = 120;

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
        var pending = new List<PendingAdmin>();

        foreach (var record in parseResult.Records)
        {
            var rowNumber = int.TryParse(record.GetValueOrDefault("_RowNumber", "0"), out var n) ? n : 0;
            var onsCode = CsvParser.GetValue<string>(record, BulkImportLocalAuthoritiesCommand.OnsCodeColumn);
            var name = CsvParser.GetValue<string>(record, "name");

            // Resolved before the first `continue`, not just before the upsert: a row rejected on its
            // organisation cells must still say what became of the admin it asked for, and the raw email cell
            // is what the results table shows in the LA Admin column either way.
            var admin = ResolveAdminUser(record);
            var adminEmailCell = CsvParser.GetValue<string>(record, BulkImportLocalAuthoritiesCommand.AdminEmailColumn)?.Trim();

            var forbidden = ForbiddenQuarterlyColumns.Where(record.ContainsKey).ToList();
            if (forbidden.Count > 0)
            {
                var msg = $"Quarterly profile columns are not supported and must be removed: {string.Join(", ", forbidden)}";
                rows.Add(FailedRow(rowNumber, onsCode, name, msg, admin, adminEmailCell));
                continue;
            }

            try
            {
                CsvParser.ValidateRequiredValues(record, RequiredColumns);
            }
            catch (ArgumentException ex)
            {
                rows.Add(FailedRow(rowNumber, onsCode, name, ex.Message, admin, adminEmailCell));
                continue;
            }

            var region = CsvParser.GetValue<string>(record, "region");
            var type = CsvParser.GetValue<string>(record, "type");
            var eCode = CsvParser.GetValue<string>(record, "e_code");
            var allocatedAmount = CsvParser.GetValue<decimal?>(record, "allocated_amount") ?? 0m;

            if (allocatedAmount <= 0)
            {
                rows.Add(FailedRow(rowNumber, onsCode, name, "allocated_amount must be greater than 0", admin, adminEmailCell));
                continue;
            }

            // The same class of bug as the e_code rule below, for the three other cells that reach a bounded
            // column: name is varchar(255), ons_code varchar(64), and allocated_amount numeric(18,2). Each was
            // caught only by the generic catch at the end of the loop, which is exactly the poisoned-context
            // path that comment describes (BLUM-7240).
            if (allocatedAmount > FieldLimits.MaxMoney)
            {
                var msg = $"allocated_amount must be {FieldLimits.MaxMoney:N0} or less";
                rows.Add(FailedRow(rowNumber, onsCode, name, msg, admin, adminEmailCell));
                continue;
            }

            if (name!.Trim().Length > FieldLimits.Name)
            {
                var msg = $"name must be {FieldLimits.Name} characters or fewer";
                rows.Add(FailedRow(rowNumber, onsCode, name, msg, admin, adminEmailCell));
                continue;
            }

            if (onsCode is not null && onsCode.Trim().Length > FieldLimits.OnsCode)
            {
                var msg = $"ons_code must be {FieldLimits.OnsCode} characters or fewer";
                rows.Add(FailedRow(rowNumber, onsCode, name, msg, admin, adminEmailCell));
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
                rows.Add(FailedRow(rowNumber, onsCode, name, msg, admin, adminEmailCell));
                continue;
            }

            var canonicalRegion = ResolveRegion(region!);
            if (canonicalRegion is null)
            {
                var msg = $"region '{region}' is not recognised. Use one of: {string.Join(", ", Region.SupportedRegions)}";
                rows.Add(FailedRow(rowNumber, onsCode, name, msg, admin, adminEmailCell));
                continue;
            }

            var canonicalType = ResolveOrgType(type!);
            if (canonicalType is null)
            {
                var msg = $"type '{type}' is not recognised. Use {OrgTypes.LocalAuthority} (or Local Authority) for a local authority, or {OrgTypes.Vcse}.";
                rows.Add(FailedRow(rowNumber, onsCode, name, msg, admin, adminEmailCell));
                continue;
            }

            // Enforced here rather than via RequiredColumns because it depends on the row's resolved type:
            // an ONS code identifies a Local Authority geography, so a VCSE row correctly has none.
            if (canonicalType == OrgTypes.LocalAuthority && string.IsNullOrWhiteSpace(onsCode))
            {
                rows.Add(FailedRow(rowNumber, onsCode, name, "ons_code is required for Local Authority rows", admin, adminEmailCell));
                continue;
            }

            var regionVo = Region.From(canonicalRegion);
            var typeVo = OrgType.From(canonicalType);

            BulkImportOutcome outcome;
            string? reason;
            OrganisationId? organisationId;

            try
            {
                (outcome, reason, organisationId) = await UpsertRowAsync(name!, onsCode, regionVo, typeVo, eCode, allocatedAmount, window, actorId, cancellationToken);
            }
            catch (Exception ex)
            {
                // A cancelled request means the caller is gone. Recording that as a row failure would walk the
                // rest of the file writing "The operation was canceled." against every remaining row and then
                // attempt the audit append with a dead token.
                cancellationToken.ThrowIfCancellationRequested();
                rows.Add(FailedRow(rowNumber, onsCode, name, ex.Message, admin, adminEmailCell));
                continue;
            }

            // Everything decidable without an external call is decided here, in the sequential phase. Only the
            // rows that genuinely need an account created go on to phase two.
            var organisationLanded = organisationId is not null
                                     && outcome is BulkImportOutcome.Created or BulkImportOutcome.Updated;
            var settled = AdminOutcomeWithoutCreating(admin, organisationLanded);

            rows.Add(new BulkImportRowOutcome(
                rowNumber,
                onsCode,
                name,
                outcome,
                reason,
                settled?.Outcome ?? BulkImportAdminUserOutcome.NotProvided,
                adminEmailCell,
                settled?.Reason));

            if (settled is null)
            {
                // Re-importable means "matched on an ons_code", which is the only key UpsertRowAsync updates by.
                pending.Add(new PendingAdmin(
                    rows.Count - 1,
                    organisationId!,
                    admin.Spec!,
                    OrganisationIsReimportable: !string.IsNullOrWhiteSpace(onsCode)));
            }
        }

        await ProvisionAdminUsersAsync(rows, pending, cancellationToken);

        // Both admin counts are surfaced in the summary line beside the organisation counts, and for the same
        // reason: without them a file whose 153 organisations all landed reads "Failed: 0" whether 40 admin
        // blocks were rejected or 113 users were skipped when the budget ran out, and the only way to tell is
        // to read every row of the table. The skipped case is the one that needs a second upload, so it is
        // exactly the one that must not be silent.
        var result = new BulkImportResult(
            CreatedCount: rows.Count(r => r.Outcome == BulkImportOutcome.Created),
            UpdatedCount: rows.Count(r => r.Outcome == BulkImportOutcome.Updated),
            SkippedCount: rows.Count(r => r.Outcome == BulkImportOutcome.Skipped),
            FailedCount: rows.Count(r => r.Outcome == BulkImportOutcome.Failed),
            Rows: rows,
            AdminUsersCreatedCount: rows.Count(r => r.AdminUserOutcome == BulkImportAdminUserOutcome.Created),
            AdminUsersFailedCount: rows.Count(r => r.AdminUserOutcome == BulkImportAdminUserOutcome.Failed),
            AdminUsersNotAttemptedCount: rows.Count(r => r.AdminUserOutcome == BulkImportAdminUserOutcome.NotAttempted));

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

    private async Task<(BulkImportOutcome Outcome, string? Reason, OrganisationId? OrganisationId)> UpsertRowAsync(
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
                        return (BulkImportOutcome.Failed, $"An organisation named '{trimmedName}' already exists, so '{org.Name}' cannot be renamed to it.", null);
                    }
                }

                org.UpdateDetails(trimmedName, region, type, trimmedECode, trimmedOnsCode, dateTimeProvider.GetUtcNow());
                await organisationRepository.UpdateAsync(org, cancellationToken);

                var allocation = await allocationRepository.FindForOrganisationAndWindowAsync(org.Id, window.Id, cancellationToken);
                if (allocation.IsSome)
                {
                    var a = allocation.ValueUnsafe();
                    a.UpdateAmount(allocatedAmount, actorId, dateTimeProvider.GetUtcNow());
                    await allocationRepository.UpdateAsync(a, cancellationToken);
                }
                else
                {
                    await allocationRepository.AddAsync(
                        OrgFundingAllocation.New(OrgFundingAllocationId.New(), org.Id, window.Id, allocatedAmount, actorId, dateTimeProvider.GetUtcNow()),
                        cancellationToken);
                }

                return (BulkImportOutcome.Updated, null, org.Id);
            }
        }

        // Mirrors CreateLocalAuthorityCommand, which rejects a duplicate name outright. Reached when the row's
        // ONS code is not on file yet (or it is a VCSE row with none) but reuses an existing organisation's
        // name — updating that organisation requires its own code, not a second row.
        var duplicateName = await organisationRepository.FindByNameAsync(trimmedName, cancellationToken);
        if (duplicateName.IsSome)
        {
            return (BulkImportOutcome.Failed, $"An organisation named '{trimmedName}' already exists. Use its existing ONS code to update it instead.", null);
        }

        var newOrg = Organisation.New(
            OrganisationId.New(),
            trimmedName,
            trimmedOnsCode,
            region,
            type,
            trimmedECode,
            OnboardingStatus.Invited,
            dateTimeProvider.GetUtcNow());
        var savedOrg = await organisationRepository.AddAsync(newOrg, cancellationToken);

        await allocationRepository.AddAsync(
            OrgFundingAllocation.New(OrgFundingAllocationId.New(), savedOrg.Id, window.Id, allocatedAmount, actorId, dateTimeProvider.GetUtcNow()),
            cancellationToken);

        return (BulkImportOutcome.Created, null, savedOrg.Id);
    }

    private static readonly Dictionary<string, string> AccessLevelAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        [LaAdminLabel] = Role.OrganisationAdmin,
        [Role.OrganisationAdmin] = Role.OrganisationAdmin,
        [LaLeadLabel] = Role.Lead,
        [Role.Lead] = Role.Lead,
    };

    private sealed record AdminUserSpec(string FirstName, string LastName, string Role, string Email);

    /// <summary>
    /// Reads the four la_admin_* cells. Returns (null, null) when the row asks for no admin at all, an error
    /// against the CSV column name when it asks for one it cannot describe, and a spec otherwise.
    ///
    /// The rules deliberately mirror CreateOrganisationUserCommandValidator rather than being stricter. Two
    /// reasons: a value the Add user form accepts must not be refused by the CSV, and pre-checking here is
    /// what stops ValidationBehaviour throwing inside the nested Send — UnhandledExceptionBehaviour logs that
    /// at Error with {@Request}, so a file full of typo'd addresses would otherwise write one error-level log
    /// line per row, each carrying a real person's email.
    /// </summary>
    private static (AdminUserSpec? Spec, string? Error) ResolveAdminUser(Dictionary<string, string> record)
    {
        var firstName = CsvParser.GetValue<string>(record, BulkImportLocalAuthoritiesCommand.AdminFirstNameColumn)?.Trim();
        var lastName = CsvParser.GetValue<string>(record, BulkImportLocalAuthoritiesCommand.AdminLastNameColumn)?.Trim();
        var accessLevel = CsvParser.GetValue<string>(record, BulkImportLocalAuthoritiesCommand.AdminAccessLevelColumn)?.Trim();
        var email = CsvParser.GetValue<string>(record, BulkImportLocalAuthoritiesCommand.AdminEmailColumn)?.Trim();

        string[] cells = [firstName!, lastName!, accessLevel!, email!];
        var filled = cells.Count(c => !string.IsNullOrWhiteSpace(c));

        if (filled == 0)
        {
            return (null, null);
        }

        if (filled < cells.Length)
        {
            return (null,
                $"{BulkImportLocalAuthoritiesCommand.AdminFirstNameColumn}, {BulkImportLocalAuthoritiesCommand.AdminLastNameColumn}, " +
                $"{BulkImportLocalAuthoritiesCommand.AdminAccessLevelColumn} and {BulkImportLocalAuthoritiesCommand.AdminEmailColumn} " +
                "must all be filled in to create a user. Leave all four blank to import the organisation on its own.");
        }

        if (firstName!.Length > FieldLimits.Name)
        {
            return (null, $"{BulkImportLocalAuthoritiesCommand.AdminFirstNameColumn} must be {FieldLimits.Name} characters or fewer");
        }

        if (lastName!.Length > FieldLimits.Name)
        {
            return (null, $"{BulkImportLocalAuthoritiesCommand.AdminLastNameColumn} must be {FieldLimits.Name} characters or fewer");
        }

        if (email!.Length > FieldLimits.Email)
        {
            return (null, $"{BulkImportLocalAuthoritiesCommand.AdminEmailColumn} must be {FieldLimits.Email} characters or fewer");
        }

        if (!IsValidEmail(email))
        {
            return (null, $"{BulkImportLocalAuthoritiesCommand.AdminEmailColumn} '{email}' is not a valid email address");
        }

        var role = AccessLevelAliases.GetValueOrDefault(accessLevel!);
        if (role is null)
        {
            return (null, $"{BulkImportLocalAuthoritiesCommand.AdminAccessLevelColumn} '{accessLevel}' is not recognised. Use {LaAdminLabel} or {LaLeadLabel}.");
        }

        return (new AdminUserSpec(firstName, lastName!, role, email), null);
    }

    // FluentValidation's EmailAddress() in AspNetCoreCompatible mode, restated: exactly one '@', neither
    // first nor last. Copied rather than tightened so the CSV and the Add user form agree on what an address
    // is; see ResolveAdminUser.
    private static bool IsValidEmail(string value)
    {
        var index = value.IndexOf('@');
        return index > 0 && index != value.Length - 1 && index == value.LastIndexOf('@');
    }

    /// <summary>
    /// What to report about the row's admin without going near CIAM, or null when there is nothing to say yet
    /// and the user should be created. Shared by the failure paths and the success path so the two cannot
    /// disagree about what a row that asked for no admin looks like.
    ///
    /// A malformed admin block is reported even when the organisation itself failed: the alternative sends the
    /// admin back to fix the region, re-upload 153 rows, and only then learn the email was wrong too.
    /// </summary>
    private static (BulkImportAdminUserOutcome Outcome, string? Reason)? AdminOutcomeWithoutCreating(
        (AdminUserSpec? Spec, string? Error) admin,
        bool organisationLanded)
    {
        if (admin.Error is not null)
        {
            return (BulkImportAdminUserOutcome.Failed, admin.Error);
        }

        if (admin.Spec is null)
        {
            return (BulkImportAdminUserOutcome.NotProvided, null);
        }

        // Not NotProvided: the row plainly asked for an admin, and the results table shows that address. Saying
        // "not provided" beside it reads as the file having omitted them rather than as the row needing a fix.
        return organisationLanded ? null : (BulkImportAdminUserOutcome.NotAttempted, AdminNotAttemptedReason);
    }

    private static BulkImportRowOutcome FailedRow(
        int rowNumber,
        string? onsCode,
        string? name,
        string reason,
        (AdminUserSpec? Spec, string? Error) admin,
        string? adminEmailCell)
    {
        var (adminOutcome, adminReason) = AdminOutcomeWithoutCreating(admin, organisationLanded: false)!.Value;
        return new BulkImportRowOutcome(rowNumber, onsCode, name, BulkImportOutcome.Failed, reason, adminOutcome, adminEmailCell, adminReason);
    }

    /// <summary>
    /// Creates the row's LA Admin through CreateOrganisationUserCommand rather than reaching for the
    /// repositories directly. That command already owns role assignability, global email uniqueness, the
    /// CIAM find-or-create, the OrganisationUser link, the invitation email and the user.created audit row —
    /// duplicating any of it here is how the two paths would drift.
    ///
    /// The collaborators are passed in rather than taken from the handler's own fields because each pool
    /// worker resolves its own from its own DI scope; the sequential tail passes the handler's.
    /// </summary>
    private static (BulkImportAdminUserOutcome Outcome, string? Reason) BudgetExhausted(PendingAdmin pending) =>
        (BulkImportAdminUserOutcome.NotAttempted,
            pending.OrganisationIsReimportable ? AdminBudgetExhaustedReason : AdminBudgetExhaustedNoOnsCodeReason);

    /// <summary>
    /// Provisions one user under its own ceiling. See the budget block in ProvisionAdminUsersAsync for why the
    /// ceiling exists; the classification below is why it is a linked token rather than the request's.
    /// </summary>
    private static async Task<(BulkImportAdminUserOutcome Outcome, string? Reason)> ProvisionWithinTimeoutAsync(
        ISender scopedSender,
        IOrganisationUserRepository scopedOrganisationUsers,
        PendingAdmin pending,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using var perUser = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        perUser.CancelAfter(timeout);

        var outcome = await ProvisionAdminUserAsync(
            scopedSender, scopedOrganisationUsers, pending, perUser.Token, cancellationToken);

        // Told apart by which token fired. The request's own cancellation has already been rethrown inside
        // ProvisionAdminUserAsync, so reaching here with the linked token cancelled means this user ran out of
        // its own time — which is a row-level failure with a reason worth reading, not "A task was canceled."
        //
        // Only a Failed outcome is relabelled. CancelAfter is a timer and can fire at any moment, including
        // just after the user was genuinely created; reporting that row as failed would be untrue, and would
        // send the admin chasing an account that already exists.
        return outcome.Outcome == BulkImportAdminUserOutcome.Failed
               && perUser.IsCancellationRequested
               && !cancellationToken.IsCancellationRequested
            ? (BulkImportAdminUserOutcome.Failed, AdminTimedOutReason)
            : outcome;
    }

    private static async Task<(BulkImportAdminUserOutcome Outcome, string? Reason)> ProvisionAdminUserAsync(
        ISender scopedSender,
        IOrganisationUserRepository scopedOrganisationUsers,
        PendingAdmin pending,
        CancellationToken cancellationToken,
        CancellationToken requestCancellationToken)
    {
        try
        {
            var result = await scopedSender.Send(
                new CreateOrganisationUserCommand
                {
                    FirstName = pending.Spec.FirstName,
                    LastName = pending.Spec.LastName,
                    Email = pending.Spec.Email,
                    Role = pending.Spec.Role,

                    // The invitation email goes out unconditionally, so a deferred account would tell someone
                    // to sign in to something that refuses them.
                    IsActive = true,
                    OrganisationId = pending.OrganisationId.Value,
                },
                cancellationToken);

            return await result.MatchAsync<(BulkImportAdminUserOutcome, string?)>(
                _ => Task.FromResult((BulkImportAdminUserOutcome.Created, (string?)null)),
                error => error is UserExistsException or DuplicateEmailException
                    ? ExistingUserOutcomeAsync(scopedOrganisationUsers, error, pending.OrganisationId, cancellationToken)

                    // Deliberately not error.Message. The likeliest failure here is UserUnknownException,
                    // whose message is "Unknown error for user with id 00000000-0000-0000-0000-000000000000" —
                    // an all-zeros GUID and nothing the reader can act on.
                    : Task.FromResult((BulkImportAdminUserOutcome.Failed, (string?)AdminCreationFailedReason)));
        }
        catch (Exception ex)
        {
            // The request's token, deliberately, not the one the work ran on. The caller gone means abandon the
            // file; this user's own ceiling expiring means fail this row and carry on with the rest.
            requestCancellationToken.ThrowIfCancellationRequested();

            // Backstop for anything ResolveAdminUser did not pre-empt, including a ValidationException out of
            // the nested pipeline. It fails the row's user, never the file.
            return (BulkImportAdminUserOutcome.Failed, ex.Message);
        }
    }

    /// <summary>
    /// Decides what an already-registered address means for this row. Re-uploading a corrected file is the
    /// normal way to fix one row, so the same person coming back round is an ordinary outcome — but only when
    /// they already belong to this organisation. An address held by someone at a different organisation is a
    /// failure the reader has to see: the command creates no link, so the organisation is left with no user
    /// from this row at all, and reporting it as an informational "already exists" hides that entirely.
    /// </summary>
    private static async Task<(BulkImportAdminUserOutcome Outcome, string? Reason)> ExistingUserOutcomeAsync(
        IOrganisationUserRepository scopedOrganisationUsers,
        UserException error,
        OrganisationId organisationId,
        CancellationToken cancellationToken)
    {
        var existingOrganisationId = await scopedOrganisationUsers.FindOrganisationIdForUser(error.UserId, cancellationToken);

        if (existingOrganisationId.IsSome && existingOrganisationId.ValueUnsafe() == organisationId)
        {
            return (BulkImportAdminUserOutcome.AlreadyExists, "This email address already has an account for this organisation, so no new account was created.");
        }

        return (BulkImportAdminUserOutcome.Failed,
            "This email address already has an account at another organisation. No user was created for this organisation — use a different address.");
    }

    private sealed record PendingAdmin(
        int RowIndex,
        OrganisationId OrganisationId,
        AdminUserSpec Spec,
        bool OrganisationIsReimportable);

    /// <summary>
    /// Phase two: create the users the sequential pass queued, several at a time, and write each outcome back
    /// onto its own row.
    ///
    /// A fixed pool of workers draining one queue, rather than a semaphore over a task per row. Each worker
    /// takes its own DI scope and keeps it for the whole run, because everything it needs is registered scoped
    /// — DbContext, GraphServiceClient, ISendGridClient, the repositories, PermissionsService — so a scope per
    /// row would mean a ClientSecretCredential per row, and that type caches its token per instance. 153 scopes
    /// would be 153 token fetches, which is most of the speed-up given away. A fixed pool costs one per worker.
    ///
    /// Owning a scope each is also what makes this safe rather than merely fast: no two workers share a
    /// DbContext, so none of the usual concurrency reasoning about EF applies. Nothing else in api/src fans work
    /// out across scopes today, which is why the structure is explicit rather than a Parallel.ForEachAsync.
    /// </summary>
    private async Task ProvisionAdminUsersAsync(
        List<BulkImportRowOutcome> rows,
        List<PendingAdmin> pending,
        CancellationToken cancellationToken)
    {
        if (pending.Count == 0)
        {
            return;
        }

        // Two rows naming the same address must not run concurrently. Both would pass the duplicate check
        // before either wrote, both would be handed the same subId by Graph, and both would write a users row —
        // after which UserProfileRepository.GetUserProfile's SingleOrDefaultAsync on sub_id throws for either
        // account, which is an unrecoverable 500 at sign-in fixable only in the database. The ordinal-vs-Graph
        // spelling of that defect was fixed in UserRepository.FindByEmailAsync; concurrency would reintroduce it
        // as a race. So only the first row for each address is fanned out, and the repeats are handled
        // sequentially afterwards, where they see the committed row and get the ordinary AlreadyExists or
        // cross-organisation answer without that decision being restated here.
        // Fully qualified: LanguageExt exports its own HashSet<A>, and this file uses LanguageExt throughout.
        var claimed = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var fanOut = new List<PendingAdmin>(pending.Count);
        var repeats = new List<PendingAdmin>();

        foreach (var item in pending)
        {
            (claimed.Add(item.Spec.Email) ? fanOut : repeats).Add(item);
        }

        // Two ceilings, and both are needed for the response to be bounded at all.
        //
        // The deadline stops new users from *starting*. On its own that bounds nothing: six workers can each
        // begin a user a moment before it expires, and one user is two Graph round trips — whose SDK retry
        // handler honours Retry-After up to three times, and directory throttling routinely answers with 60
        // seconds — plus a SendGrid send with its own 15s timeout. That last batch alone could run for minutes
        // past the deadline and hand the caller exactly the gateway error this design exists to remove.
        //
        // So each user also gets its own ceiling, and the worst case is deadline + per-user rather than
        // unbounded. At the defaults that is 150s + 30s against the platform's 230s, leaving room for the
        // response itself.
        //
        // The per-user token is linked, not the request token with a CancelAfter on it: a timeout must fail
        // one row, never the file. It can abandon a user between the Graph account being created and the local
        // row being written — but that hazard already exists on any exception here, and it self-heals, because
        // CreateOrganisationUserCommand looks the address up in the directory before creating it.
        var budget = Stopwatch.StartNew();
        var deadline = TimeSpan.FromSeconds(
            Math.Clamp(settings.BulkImport.UserProvisioningBudgetSeconds, MinBudgetSeconds, MaxBudgetSeconds));
        var perUserTimeout = TimeSpan.FromSeconds(
            Math.Clamp(settings.BulkImport.UserProvisioningTimeoutSeconds, MinUserTimeoutSeconds, MaxUserTimeoutSeconds));

        var queue = new ConcurrentQueue<PendingAdmin>(fanOut);
        var outcomes = new (BulkImportAdminUserOutcome Outcome, string? Reason)?[rows.Count];

        var workerCount = Math.Min(
            Math.Clamp(settings.BulkImport.MaxConcurrentUserCreations, MinWorkers, MaxWorkers),
            fanOut.Count);

        var workers = Enumerable.Range(0, workerCount).Select(async _ =>
        {
            // Resolved on the request's execution context, so IHttpContextAccessor — a singleton over an
            // AsyncLocal — still yields this request's HttpContext, and IdentityService can be constructed.
            // That is the whole reason this work can stay in-request and could not simply be moved behind the
            // response: IdentityService guards HttpContext for null in its constructor and throws without one.
            // CreateAsyncScope, not CreateScope: RepositoryBase implements IAsyncDisposable and nothing else, so
            // a synchronous Dispose on a scope holding one throws "type only implements IAsyncDisposable".
            await using var scope = scopeFactory.CreateAsyncScope();
            var scopedSender = scope.ServiceProvider.GetRequiredService<ISender>();
            var scopedOrganisationUsers = scope.ServiceProvider.GetRequiredService<IOrganisationUserRepository>();

            while (queue.TryDequeue(out var item))
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (budget.Elapsed >= deadline)
                {
                    outcomes[item.RowIndex] = BudgetExhausted(item);
                    continue;
                }

                outcomes[item.RowIndex] = await ProvisionWithinTimeoutAsync(
                    scopedSender, scopedOrganisationUsers, item, perUserTimeout, cancellationToken);
            }
        }).ToList();

        await Task.WhenAll(workers);

        foreach (var item in repeats)
        {
            cancellationToken.ThrowIfCancellationRequested();

            outcomes[item.RowIndex] = budget.Elapsed >= deadline
                ? BudgetExhausted(item)
                : await ProvisionWithinTimeoutAsync(
                    sender, organisationUserRepository, item, perUserTimeout, cancellationToken);
        }

        for (var index = 0; index < outcomes.Length; index++)
        {
            if (outcomes[index] is not { } outcome)
            {
                continue;
            }

            rows[index] = rows[index] with
            {
                AdminUserOutcome = outcome.Outcome,
                AdminUserReason = outcome.Reason,
            };
        }
    }
}