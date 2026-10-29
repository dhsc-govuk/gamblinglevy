using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

// BLUM-7563: "key results" is internal modelling vocabulary and has been removed from the LA-facing language.
// The "Manage your quarterly report" task list renders its section titles from form_modules.name, so the link
// that read "Enter activity key results" is display data, not client copy. SeedDataCollectionFormModulesAsync
// now creates the module as "Enter activity results", but that seeder is guarded by FormModules.AnyAsync(), so
// the new name can never reach a database that has already been seeded. This is that one-shot rename.
//
// An UPDATE rather than the delete-and-reseed used for the protocol tables: form_modules is not disposable
// reference data. It owns the AKR_DELIVERY_* questions (cascade), and every submission_sections row carries its
// FormModuleId, so deleting the module would take the quarterly-report schema and the section rows of every
// existing report with it.
//
// This is also all that AC4 ("across all reporting periods, not just the current quarter") needs:
// SubmissionSection snapshots only ModuleCode, and QuarterlyReportControllerService.ToTaskListDtoAsync resolves
// the display name live from form_modules on every read. So one row changes the label on submitted reports too,
// and no per-submission backfill exists to write.
//
// The name in the WHERE clause keeps a module a DHSC admin renamed through the forms builder untouched.
//
// Data only - no DDL, so there is no model change and no .Designer.cs.
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260820120000_BLUM_7563_RenameActivityResultsModule")]
public partial class BLUM_7563_RenameActivityResultsModule : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            UPDATE form_modules
            SET name = 'Enter activity results'
            WHERE code = 'activity_key_results' AND name = 'Enter activity key results';
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            UPDATE form_modules
            SET name = 'Enter activity key results'
            WHERE code = 'activity_key_results' AND name = 'Enter activity results';
            """);
    }
}