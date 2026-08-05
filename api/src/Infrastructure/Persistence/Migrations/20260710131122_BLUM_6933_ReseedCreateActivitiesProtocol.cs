using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BLUM_6933_ReseedCreateActivitiesProtocol : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "activity_task_id",
                table: "form_sections",
                type: "uuid",
                nullable: true);

            // BLUM-6933: the Question Protocol was rewritten on 3 July (activity types removed/renamed, key
            // results and report questions removed/reworded, new 1.EF "People and issues" questions). Rather
            // than surgically converging every deployed row, wipe the whole activity + submission domain so the
            // seed-if-empty seeders (SeedActivityReferenceData, SeedFormDesigner, SeedKeyResultProtocolAsync)
            // rebuild it from the updated seed content on next boot. Approved destructive reset — the service
            // is pre-launch and all activity/report data on deployed environments is test data. Users,
            // organisations, locations, audit logs, MFA, collections and the dc_* form modules are untouched.
            // Children are deleted before parents (submission_* -> submissions FK; option -> question FK).
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
                DELETE FROM activity_tasks;
                DELETE FROM categories;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "activity_task_id",
                table: "form_sections");
        }
    }
}