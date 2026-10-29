using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

// BLUM-7697: the "Who have you worked with, or who will you work with?" checkbox list had drifted from DHSC's data
// map. "Affected others" was missing entirely, and the map's three separate health options — Primary care,
// Integrated care board (ICB), Other health practitioners — were seeded as one merged checkbox. Both are fixed in
// KeyResultProtocolSeedData.WhoWithOptions, which is the single definition of that list: SeedActivityDetailsSection
// writes it to form_question_options for all five categories' {prefix}_WHO_WITH questions, and the same array backs
// the jsonb options on the five quarterly-report questions that share it (*_CONTRIBUTORS, LSG_MEMBERS,
// TRN_SERVICE_AREAS, SCR_SERVICES).
//
// Seed content lives only in the seeders — migrations never INSERT or UPDATE it. Both seeders write once
// (SeedFormDesigner guards on FormSections.AnyAsync(), SeedKeyResultProtocolAsync on KeyResultQuestions.AnyAsync()),
// so the new options cannot reach an already-seeded database without clearing those guards. This is that one-shot
// clear; the seeders rebuild both halves on the next boot. Runs exactly once per database (__EFMigrationsHistory).
//
// key_result_definitions goes with key_result_questions: that one guard covers a pass writing both, so leaving the
// definitions behind would re-insert them and violate their unique (activity_task_id, code) index — a seed failure
// is fatal to startup.
//
// Deliberately narrower than the BLUM-7620 reseed: activities, activity_answers, activity_expected_results,
// submissions and submission_activity_answers are all KEPT. Answers resolve by their snapshotted question_code
// (ActivityAnswer.QuestionId is write-only — no read path joins it), so re-minted question and key-result ids
// strand nothing, and because this change only ADDS option values no stored answer stops VALIDATING — which is why
// there is no counterpart here to BLUM-7475's targeted submission_activity_answers cleanup.
//
// It does not follow that nothing changed for existing answers, and this migration is not claiming that.
// "primary_care" keeps its value but its label narrows from "Primary care, integrated care board (ICB) and other
// health practitioners" to "Primary care", and labels are resolved LIVE from the current option list
// (ReportAnswerPresenter.RenderAnswer, and the activity and report summaries). So a row an LA ticked to record ICB
// or wider health-practitioner involvement now reads as primary care alone — on screen, and in any export
// re-rendered from a null ReportSnapshotJson, while a stored snapshot keeps the old wording, so two exports of the
// same answer can disagree. No backfill is possible: the merged value cannot say which of the three the LA meant,
// and splitting it three ways would invent an answer nobody gave. Accepted deliberately for MVP-tier data —
// nothing is live, and BLUM-7620 emptied both answer tables on 24 August — and flagged to DHSC in the PR rather
// than papered over with a guess.
//
// One accepted cost, the same one every form-schema reseed takes: the guard needs form_sections empty and the
// seeder only rebuilds the canonical set, so any section or question a DHSC admin authored through the Form
// Designer before this deploy is lost and Down cannot restore it. None expected at MVP; called out in the PR.
//
// Children are deleted before parents (option -> question -> section FK).
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260824170000_BLUM_7697_ReseedWhoWithOptions")]
public partial class BLUM_7697_ReseedWhoWithOptions : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DELETE FROM form_question_options;
            DELETE FROM form_questions;
            DELETE FROM form_sections;
            DELETE FROM key_result_questions;
            DELETE FROM key_result_definitions;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Nothing to undo — the deleted rows are seed content, and the seeders rebuild both halves on the next
        // boot regardless of which direction we arrived from.
    }
}