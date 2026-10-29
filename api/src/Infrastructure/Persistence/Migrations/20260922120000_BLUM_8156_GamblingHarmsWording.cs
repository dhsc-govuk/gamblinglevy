using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

// BLUM-8156: DHSC's term is "gambling harms". The platform still said "harm" in one activity type name, in several
// key-result protocol questions and error messages, and in one key-result label. KeyResultProtocolSeedData and
// SeedActivityReferenceData now hold the new wording, but both are seed-if-empty, so they only affect a fresh
// database. This migration brings an already-seeded database into line.
//
// It only updates rows. The service is live, so it deletes nothing. Earlier protocol tickets cleared
// key_result_questions/definitions and let the seeder rebuild them; that is no longer allowed. Each statement:
//   • matches on the OLD text, so it only touches a row still in its seeded state, and re-running it does nothing;
//   • keeps every id, so nothing that refers to these rows is affected. Activities point at activity_tasks by id,
//     and saved answers and chosen outcomes point at key results by code.
//
//   1. activity_tasks: "Using screening tools to better assess gambling harm" becomes "...harms". This one row feeds
//      the wizard, the View page, report headings, funding messages and the activity-record CSV, so the client's
//      TYPE_DISPLAY_NAMES entry that covered some of those screens has been removed.
//   2. key_result_questions: labels and error messages, matched on (code, old text). A question's Active and
//      Closed/Finished versions share a code but have different labels, so the old text also picks the version.
//   3. key_result_definitions: the wpc_policies outcome label.
//   4. activity_expected_results: when an LA picks a standard outcome, its label is copied into the row's text.
//      Screens look the label up by code, so this copy is not shown. But ExpectedResultSync compares it with the
//      current label, so the next edit of any such activity would record a "harm" -> "harms" change the LA never
//      made. Only rows for the wpc_policies code that still hold the old seeded label are updated; custom
//      free-text outcomes (null code) are not touched.
//
// Frozen snapshots of reports that were already submitted keep the wording the LA was asked. This is deliberate.
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260922120000_BLUM_8156_GamblingHarmsWording")]
public partial class BLUM_8156_GamblingHarmsWording : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            UPDATE activity_tasks
            SET task_name = 'Using screening tools to better assess gambling harms', updated_at = now()
            WHERE task_name = 'Using screening tools to better assess gambling harm';

            UPDATE key_result_questions q
            SET label = v.new_label
            FROM (VALUES
                ('CAM_LIVE', 'Will you deliver any local awareness campaigns to address gambling-related harm?', 'Will you deliver any local awareness campaigns to address gambling-related harms?'),
                ('CAM_LIVE', 'Have you delivered any local awareness campaigns to address gambling-related harm?', 'Have you delivered any local awareness campaigns to address gambling-related harms?'),
                ('LEX_ENGAGED', 'Are you involving people with lived experience of gambling harm?', 'Are you involving people with lived experience of gambling harms?'),
                ('LEX_ENGAGED', 'Have you involved people with lived experience of gambling harm?', 'Have you involved people with lived experience of gambling harms?'),
                ('SCR_TOOL', 'Have you identified or designed a gambling harm screening tool for local use?', 'Have you identified or designed a gambling harms screening tool for local use?'),
                ('SCR_SERVICES', 'Which services are currently screening for gambling harm?', 'Which services are currently screening for gambling harms?'),
                ('SCR_SERVICES_OTHER', 'Which other services are currently screening for gambling harm?', 'Which other services are currently screening for gambling harms?')
            ) AS v(code, old_label, new_label)
            WHERE q.code = v.code AND q.label = v.old_label;

            UPDATE key_result_questions q
            SET error_message = v.new_message
            FROM (VALUES
                ('SCR_SERVICES', 'Select which services are screening for gambling harm', 'Select which services are screening for gambling harms'),
                ('SCR_SERVICES_OTHER', 'Enter which other services are currently screening for gambling harm', 'Enter which other services are currently screening for gambling harms')
            ) AS v(code, old_message, new_message)
            WHERE q.code = v.code AND q.error_message = v.old_message;

            UPDATE key_result_definitions
            SET label = 'Policies and support for staff affected by gambling harms'
            WHERE code = 'wpc_policies' AND label = 'Policies and support for staff affected by gambling harm';

            UPDATE activity_expected_results
            SET text = 'Policies and support for staff affected by gambling harms'
            WHERE key_result_code = 'wpc_policies' AND text = 'Policies and support for staff affected by gambling harm';
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            UPDATE activity_tasks
            SET task_name = 'Using screening tools to better assess gambling harm', updated_at = now()
            WHERE task_name = 'Using screening tools to better assess gambling harms';

            UPDATE key_result_questions q
            SET label = v.old_label
            FROM (VALUES
                ('CAM_LIVE', 'Will you deliver any local awareness campaigns to address gambling-related harm?', 'Will you deliver any local awareness campaigns to address gambling-related harms?'),
                ('CAM_LIVE', 'Have you delivered any local awareness campaigns to address gambling-related harm?', 'Have you delivered any local awareness campaigns to address gambling-related harms?'),
                ('LEX_ENGAGED', 'Are you involving people with lived experience of gambling harm?', 'Are you involving people with lived experience of gambling harms?'),
                ('LEX_ENGAGED', 'Have you involved people with lived experience of gambling harm?', 'Have you involved people with lived experience of gambling harms?'),
                ('SCR_TOOL', 'Have you identified or designed a gambling harm screening tool for local use?', 'Have you identified or designed a gambling harms screening tool for local use?'),
                ('SCR_SERVICES', 'Which services are currently screening for gambling harm?', 'Which services are currently screening for gambling harms?'),
                ('SCR_SERVICES_OTHER', 'Which other services are currently screening for gambling harm?', 'Which other services are currently screening for gambling harms?')
            ) AS v(code, old_label, new_label)
            WHERE q.code = v.code AND q.label = v.new_label;

            UPDATE key_result_questions q
            SET error_message = v.old_message
            FROM (VALUES
                ('SCR_SERVICES', 'Select which services are screening for gambling harm', 'Select which services are screening for gambling harms'),
                ('SCR_SERVICES_OTHER', 'Enter which other services are currently screening for gambling harm', 'Enter which other services are currently screening for gambling harms')
            ) AS v(code, old_message, new_message)
            WHERE q.code = v.code AND q.error_message = v.new_message;

            UPDATE key_result_definitions
            SET label = 'Policies and support for staff affected by gambling harm'
            WHERE code = 'wpc_policies' AND label = 'Policies and support for staff affected by gambling harms';

            UPDATE activity_expected_results
            SET text = 'Policies and support for staff affected by gambling harm'
            WHERE key_result_code = 'wpc_policies' AND text = 'Policies and support for staff affected by gambling harms';
            """);
    }
}