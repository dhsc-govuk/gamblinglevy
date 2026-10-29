using System.Text.Json;
using Application.Common.Interfaces;
using Application.MaturityAssessments.Prefill;
using Domain.AuditLogs;
using Domain.MaturityAssessments;
using Domain.Organisations;
using Domain.Users;
using Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Infrastructure.Persistence.Seed.MaturityBaseline;

// Loads the 2025 maturity survey as each local authority's prior-year baseline (BLUM-7857). Runs last in
// SeedDataAsync on every boot and after a data reset, and converges: an organisation that already holds rows for
// the survey year is skipped, so the import is re-runnable and a local authority created later is picked up by the
// next boot. Every failure is logged and swallowed here, so unlike the other seeding steps (BLUM-8749) this one never
// fails the seeding run, not even in the test-data reset or the integration suite.
//
// The import is audited (AC4) as one maturity_assessment.baseline_imported row per boot that imported at least one
// organisation, carrying the matched / unmatched / skipped lists so DHSC can see which survey rows found no home.
public class MaturityBaselineSeeder(ApplicationDbContext dbContext, ILogger<MaturityBaselineSeeder> logger, IDateTimeProvider dateTimeProvider)
{
    public const string AlreadyImported = "already_imported";

    public const string NoAnswers = "no_answers";

    public const int SurveyYear = MaturityPrefillService.BaselineSurveyYear;

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await ImportAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Two instances booting together both pass the per-organisation guard; the unique index on
            // (organisation_id, survey_year, question_code) makes the loser fail here. Drop the tracked rows so the
            // shared context is clean for whatever runs next, and let the winner's rows stand.
            dbContext.ChangeTracker.Clear();
            logger.LogWarning(ex, "Maturity baseline seed: another instance imported the survey first; skipping.");
        }
        catch (Exception ex)
        {
            dbContext.ChangeTracker.Clear();
            logger.LogError(ex, "Maturity baseline seed: the 2025 survey import failed; the portal continues without it.");
        }
    }

    private async Task ImportAsync(CancellationToken cancellationToken)
    {
        var rows = MaturityBaselineCsv.LoadEmbedded();

        // Type is a value object persisted as a string; comparing in memory sidesteps the EF translation question.
        var organisations = await dbContext.Organisations.AsNoTracking().ToListAsync(cancellationToken);
        var localAuthorities = organisations
            .Where(o => o.Type.Equals(OrgType.LocalAuthority))
            .Select(o => new LocalAuthorityCandidate(o.Id.Value, o.Name, o.Region.ToString()))
            .ToList();

        var alreadyImported = (await dbContext.MaturityBaselineAnswers
                .AsNoTracking()
                .Where(a => a.SurveyYear == SurveyYear)
                .Select(a => a.OrganisationId)
                .Distinct()
                .ToListAsync(cancellationToken))
            .Select(id => id.Value)
            .ToHashSet();

        var match = LocalAuthorityNameMatcher.Match(rows, localAuthorities);

        var imported = new List<object>();
        var skipped = match.Skipped.Select(s => new { row = s.Row.RowNumber, localAuthority = s.Row.LocalAuthority, reason = s.Reason }).ToList<object>();
        var answersImported = 0;

        foreach (var matched in match.Matched)
        {
            if (alreadyImported.Contains(matched.OrganisationId))
            {
                skipped.Add(new { row = matched.Row.RowNumber, localAuthority = matched.Row.LocalAuthority, reason = AlreadyImported });
                continue;
            }

            IReadOnlyList<MaturityBaselineMappedAnswer> answers;
            try
            {
                answers = MaturityBaselineMapping.Map(matched.Row);
            }
            catch (Exception ex)
            {
                // One malformed row must not cost the other 151 their baseline.
                logger.LogError(ex, "Maturity baseline seed: row {Row} ({LocalAuthority}) could not be mapped.", matched.Row.RowNumber, matched.Row.LocalAuthority);
                skipped.Add(new { row = matched.Row.RowNumber, localAuthority = matched.Row.LocalAuthority, reason = "mapping_failed" });
                continue;
            }

            if (answers.Count == 0)
            {
                skipped.Add(new { row = matched.Row.RowNumber, localAuthority = matched.Row.LocalAuthority, reason = NoAnswers });
                continue;
            }

            var organisationId = new OrganisationId(matched.OrganisationId);
            dbContext.MaturityBaselineAnswers.AddRange(answers.Select(a =>
                MaturityBaselineAnswer.Import(MaturityBaselineAnswerId.New(), organisationId, a.Code, a.Value, SurveyYear, dateTimeProvider.GetUtcNow())));

            answersImported += answers.Count;
            imported.Add(new
            {
                row = matched.Row.RowNumber,
                localAuthority = matched.Row.LocalAuthority,
                organisationId = matched.OrganisationId,
                organisationName = matched.OrganisationName,
                regionMismatch = matched.RegionMismatch,
                answers = answers.Count,
            });
        }

        var unmatched = match.Unmatched.Select(u => new { row = u.Row.RowNumber, localAuthority = u.Row.LocalAuthority, region = u.Row.Region, reason = u.Reason }).ToList();

        if (imported.Count == 0)
        {
            logger.LogInformation(
                "Maturity baseline seed: nothing to import ({Rows} survey rows, {LocalAuthorities} local authorities, {Unmatched} unmatched, {Skipped} skipped).",
                rows.Count,
                localAuthorities.Count,
                unmatched.Count,
                skipped.Count);
            return;
        }

        // The seeded DHSC admin is the natural actor for a boot-time import; UserId.Empty() only when no admin has
        // been seeded yet, so the row is still written rather than the import going unrecorded.
        var admin = await dbContext.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Role == UserRole.Admin, cancellationToken);
        var summary = JsonSerializer.Serialize(new
        {
            surveyYear = SurveyYear,
            importedOrganisations = imported.Count,
            importedAnswers = answersImported,
            matched = imported,
            unmatched,
            skipped,
        });

        // Added straight to the context so it commits in the same SaveChanges as the answers above, and stamped
        // the way AuditLogRepository.AppendAsync stamps. It cannot go through that repository: this seeder is
        // resolved in DbModule's boot scope, which is disposed synchronously, and RepositoryBase implements
        // IAsyncDisposable only — injecting one here fails every boot.
        var audit = dbContext.AuditLogs.Add(AuditLog.Create(
            admin?.Id ?? UserId.Empty(),
            AuditTargetType.Organisation,
            Guid.Empty,
            AuditAction.MaturityBaselineImported,
            before: null,
            after: summary));
        audit.Property(x => x.OccurredAt).CurrentValue = dateTimeProvider.GetUtcNow();

        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Maturity baseline seed: imported {Answers} answers for {Organisations} local authorities ({Unmatched} survey rows unmatched, {Skipped} skipped).",
            answersImported,
            imported.Count,
            unmatched.Count,
            skipped.Count);
    }
}