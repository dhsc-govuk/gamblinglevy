using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

// BLUM-7924: the maturity assessment's "Non-funded roles" page asked one WTE question for every non-funded role
// ticked (MA_NONFUNDED_WTE, revealed by any role), and the renderer gives a child matching several checkbox options a
// single reveal under the first ticked one — so the WTE only ever appeared under the first role. The catalogue now
// asks it once per role (MA_NONFUNDED_WTE__PUBLIC_HEALTH_OFFICER, …), each revealed by that role alone, the pattern
// BLUM-7788 used for the quarterly LSG_ENGAGEMENT_{MEMBER} questions. The catalogue is static C# (MaturityProtocol),
// so the question change itself needs nothing here; this migration deals with the rows the old shape left behind.
//
// The first statement re-arms the 2025 baseline import. maturity_baseline_answers holds answers already mapped onto
// catalogue codes, and MaturityBaselineSeeder skips any organisation that holds rows for the survey year — so every
// local authority seeded before this deploy would keep the old combined MA_NONFUNDED_WTE band and never gain a
// per-role one. That band cannot be split back into roles (it was the sum of the per-role bands, re-banded); only the
// embedded survey CSV can, and MaturityBaselineMapping now copies each role's band across 1:1. Emptying the survey
// year lets the seeder, which runs last in SeedDataAsync on this same boot, re-import every matched authority with
// the new mapping and write one fresh maturity_assessment.baseline_imported audit row. Seed content is never written
// from a migration — this only deletes so the seeder's guard re-arms, as BLUM-7871 did for the form schema.
//
// The second statement drops answers saved under the retired code. Nothing resolves MA_NONFUNDED_WTE any more, so
// they are already invisible everywhere — every read path filters stored answers against the catalogue, and prior-
// assessment prefill copies only codes that still resolve. They are not moved onto a per-role code: one band entered
// for several roles cannot be attributed to any one of them. A submitted assessment keeps the answer as it was shown
// in its frozen snapshot_json, which is what its detail view and per-LA export read.
//
// The third statement stops that becoming silently missing data, and is needed because this change ADDS required
// questions. SubmitMaturityAssessmentCommand gates on stored section statuses before it re-validates the catalogue, so
// an LA that had already completed "LA capacity" with a role ticked would be shown as ready to submit, then refused
// with a 422 that names no section. Rolling that section back to in_progress sends them through the page, where each
// ticked role now asks for its own WTE. Narrowed to what is affected: unsubmitted assessments whose non-funded roles
// tick at least one role ("none" reveals nothing). This is user progress, not seed content.
//
// DEPLOY NOTE, as for BLUM-7871: between MigrateAsync emptying the baseline and SeedDataAsync refilling it a moment
// later, an assessment Started in that window gets no 2025 pre-fill. Deploy as a single-instance restart. And a draft
// started before this deploy keeps what Start pre-filled then — the baseline is read only at Start — so its LA enters
// each role's WTE by hand.
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260911120000_BLUM_7924_PerRoleNonFundedWte")]
public partial class BLUM_7924_PerRoleNonFundedWte : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DELETE FROM maturity_baseline_answers WHERE survey_year = 2025;

            DELETE FROM maturity_assessment_answers WHERE question_code = 'MA_NONFUNDED_WTE';

            UPDATE maturity_assessment_sections s
            SET status = 'in_progress', updated_at = now()
            FROM maturity_assessments m
            WHERE s.assessment_id = m.id
              AND m.submitted_at IS NULL
              AND s.section_code = 'la_capacity'
              AND s.status = 'completed'
              AND EXISTS (
                  SELECT 1
                  FROM maturity_assessment_answers a
                  WHERE a.assessment_id = m.id
                    AND a.question_code = 'MA_NONFUNDED_ROLES'
                    AND string_to_array(a.value, ',') && ARRAY[
                        'public_health_officer',
                        'public_health_analyst',
                        'public_health_practitioner',
                        'public_health_specialist',
                        'public_health_intelligence_specialist',
                        'consultant_in_public_health']);
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Nothing to undo. The baseline rows are seed content the seeder rebuilds on every boot that finds them
        // missing, and by the time anything could run Down that boot has already re-imported them; redeploying the
        // previous app version would not re-map them to the single WTE either, because the seeder's guard is armed
        // again. The deleted MA_NONFUNDED_WTE answers are unrecoverable, and the sections rolled back to in_progress
        // stay there: re-marking them completed here would hand back the "ready to submit, then refused" state Up
        // exists to prevent.
    }
}