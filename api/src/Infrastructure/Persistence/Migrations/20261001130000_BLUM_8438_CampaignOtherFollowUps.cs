using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

// BLUM-8438: "Other" on the maturity assessment's "Have you delivered any local awareness campaigns on gambling harms
// in the past year?" now routes like "Yes" and "In progress" — on to the target audience, the aim and the resources
// link (mapping file source row 102). The catalogue is static C# (MaturityProtocol), so the routing change itself
// needs nothing here; this migration deals with the drafts the old routing left behind.
//
// It is needed because the change makes two questions newly REQUIRED for an LA that answered "Other": the audience
// and the aim. SubmitMaturityAssessmentCommand gates on stored section statuses before it re-validates the catalogue,
// so an LA that had already completed "Local campaigns" with "Other" would be shown as ready to submit, then refused
// with a 422 that names no section. Rolling that section back to in_progress sends them through the page, where the
// follow-ups are now revealed. The BLUM-7924 rollback is the precedent.
//
// Every such section is rolled back, including one whose follow-up answers happen to be stored. Start persists
// pre-filled answers whatever their visibility, so those would be 2025-baseline values the LA has never been shown,
// and they should not be submitted unseen. This is user progress, not seed content.
//
// A submitted assessment is left alone: it reads its frozen snapshot_json. The 2025 baseline is not re-imported
// either — MaturityBaselineMapping is unchanged and always mapped the three follow-ups regardless of the campaign
// answer.
[DbContext(typeof(ApplicationDbContext))]
[Migration("20261001130000_BLUM_8438_CampaignOtherFollowUps")]
public partial class BLUM_8438_CampaignOtherFollowUps : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            UPDATE maturity_assessment_sections s
            SET status = 'in_progress', updated_at = now()
            FROM maturity_assessments m
            WHERE s.assessment_id = m.id
              AND m.submitted_at IS NULL
              AND s.section_code = 'local_campaigns'
              AND s.status = 'completed'
              AND EXISTS (
                  SELECT 1
                  FROM maturity_assessment_answers a
                  WHERE a.assessment_id = m.id
                    AND a.question_code = 'MA_CAMPAIGN'
                    AND a.value = 'other');
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Nothing to undo. The sections rolled back to in_progress stay there: re-marking them completed here would
        // hand back the "ready to submit, then refused" state Up exists to prevent.
    }
}