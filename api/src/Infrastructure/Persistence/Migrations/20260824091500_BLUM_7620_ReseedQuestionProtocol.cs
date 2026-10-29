using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

// BLUM-7620: DHSC re-issued the Question Protocol on 13 August. The blue-highlighted changes reach across both
// halves of it — the activity-record form schema (question wording, hint text, three option lists, and the new
// error-message / character-limit columns), the results-table key results and questions (a strategy result split
// in two, a new radio field type, two new steering-group questions, a dozen reworded error messages), and the
// "Activity delivery" questions on the activity_key_results module.
//
// Seed content lives only in KeyResultProtocolSeedData and ApplicationDbContextInitialiser — migrations never
// INSERT or UPDATE it. Each of those seeders is guarded so it writes once (SeedFormDesigner and
// SeedKeyResultProtocolAsync by an AnyAsync() on their own tables; SeedQuarterlyReportModuleAsync by the module
// already being Published), so the changed rows cannot reach a seeded database without clearing those guards.
// This is that one-shot clear; the seeders rebuild everything on the next boot.
//
// Approved destructive reset: nothing is live yet and all activity/report data on deployed environments is test
// data. Users, organisations, locations, audit logs, MFA, collections and the form_modules rows themselves are
// untouched — collection_forms references form_modules, so the modules are emptied and un-published rather than
// deleted. The strategic_feedback module needs nothing here: SeedStrategicFeedbackModuleAsync reconciles its
// questions row by row and so picks up the new hint and error copy on a published module by itself.
//
// Children are deleted before parents (submission_* -> submissions; option -> question; answer -> activity).
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260824091500_BLUM_7620_ReseedQuestionProtocol")]
public partial class BLUM_7620_ReseedQuestionProtocol : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DELETE FROM submission_activity_answers;
            DELETE FROM submission_feedback_answers;
            DELETE FROM submission_spend_entries;
            DELETE FROM submission_activity_reports;
            DELETE FROM submission_sections;
            DELETE FROM submissions;
            DELETE FROM activity_answers;
            DELETE FROM activity_expected_results;
            DELETE FROM activities;
            DELETE FROM key_result_questions;
            DELETE FROM key_result_definitions;
            DELETE FROM form_question_options;
            DELETE FROM form_questions;
            DELETE FROM form_sections;

            DELETE FROM dc_form_question_options
            WHERE question_id IN (
                SELECT q.id FROM dc_form_questions q
                JOIN form_modules m ON m.id = q.form_module_id
                WHERE m.code = 'activity_key_results');

            DELETE FROM dc_form_questions
            WHERE form_module_id IN (SELECT id FROM form_modules WHERE code = 'activity_key_results');

            UPDATE form_modules SET status = 'draft' WHERE code = 'activity_key_results';
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Nothing to undo — the deleted rows are seed content, and the seeders rebuild the protocol on the next
        // boot regardless of which direction we arrived from.
    }
}