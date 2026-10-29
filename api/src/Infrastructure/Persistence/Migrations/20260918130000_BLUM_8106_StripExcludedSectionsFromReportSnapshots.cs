using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

// BLUM-8106: the quarterly report snapshot frozen at submit time used to render all three form modules whatever the
// collection was published with, so a report whose collection left a form out carried that form's content anyway —
// activity results pre-filled from the activity records, a funding row per activity, every blank feedback page — and
// both the LA's download and the DHSC cross-organisation export wrote it out. ReportSnapshotService now renders only
// the sections the submission has; this clears the same parts out of the snapshots already stored.
//
// Keyed on submission_sections, which is frozen when the report is started and is the task list the LA worked from —
// never on the collection's current forms, which an admin can change after reports exist. Each part is reset to what
// the builder now writes for a section the report does not have: an empty list, or null for funding. Nothing an LA
// entered is lost: none of this content was ever on a screen they could reach. No audit row is written, as nothing a
// user did has changed.
//
// Data only - no DDL, so there is no model change and no .Designer.cs.
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260918130000_BLUM_8106_StripExcludedSectionsFromReportSnapshots")]
public partial class BLUM_8106_StripExcludedSectionsFromReportSnapshots : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            UPDATE submissions s
            SET report_snapshot_json = jsonb_set(s.report_snapshot_json, '{activitySections}', '[]'::jsonb)
            WHERE s.report_snapshot_json IS NOT NULL
              AND NOT EXISTS (
                  SELECT 1 FROM submission_sections ss
                  WHERE ss.submission_id = s.id AND ss.module_code = 'activity_key_results');

            UPDATE submissions s
            SET report_snapshot_json = jsonb_set(s.report_snapshot_json, '{funding}', 'null'::jsonb)
            WHERE s.report_snapshot_json IS NOT NULL
              AND NOT EXISTS (
                  SELECT 1 FROM submission_sections ss
                  WHERE ss.submission_id = s.id AND ss.module_code = 'funding_spent');

            UPDATE submissions s
            SET report_snapshot_json = jsonb_set(s.report_snapshot_json, '{feedbackSections}', '[]'::jsonb)
            WHERE s.report_snapshot_json IS NOT NULL
              AND NOT EXISTS (
                  SELECT 1 FROM submission_sections ss
                  WHERE ss.submission_id = s.id AND ss.module_code = 'strategic_feedback');
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Nothing to undo. The content removed was never part of the report the LA submitted, and the snapshot
        // builder no longer produces it.
    }
}