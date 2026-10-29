using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

// BLUM-7943: the Add data collection Forms step now offers a maturity assessment its own ten catalogue sections as
// forms, instead of the three quarterly-report form modules it used to list for every type. Each section has a fixed
// id (Application.MaturityAssessments.Protocol.MaturityCollectionForms) stored in collection_forms.form_module_id,
// which has no foreign key to form_modules, so the change needs no DDL.
//
// Every maturity collection created before this deploy holds quarterly form-module ids, because those were the only
// options on offer. The create/update validators now refuse another type's forms, so without this a DHSC admin could
// not save any edit to one of those collections (CollectionEditor sends the stored forms back on every save), and
// its View page would keep listing quarterly module names. The rows are replaced with every section — what the
// Forms step now preselects, and what the LA assessment has always asked regardless of collection_forms.
//
// Scoped to type only, so closed and soft-deleted collections are converted too (a restore would otherwise bring back
// the old ids). No audit row is written: nothing a DHSC user chose has changed, only the representation of it.
//
// The ids below must match MaturityCollectionForms; MaturityCollectionFormsTests reads this migration's SQL and
// fails if one is missing.
//
// Data only - no DDL, so there is no model change and no .Designer.cs.
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260916120000_BLUM_7943_MaturityCollectionForms")]
public partial class BLUM_7943_MaturityCollectionForms : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DELETE FROM collection_forms cf
            USING collections c
            WHERE cf.collection_id = c.id
              AND c.type = 'maturity_assessment';

            INSERT INTO collection_forms (collection_id, form_module_id)
            SELECT c.id, f.id
            FROM collections c
            CROSS JOIN (VALUES
                ('aa721c9a-7034-4444-b706-f61167e7022e'::uuid),
                ('a963f8c5-a06b-4a71-8f4e-33635f4f557d'::uuid),
                ('14e6c7ed-3fa0-42fa-80b0-9a5f19c36ea9'::uuid),
                ('882b90b0-0cc8-4417-b56b-111928e2bbe5'::uuid),
                ('6f9b7386-fc4f-446c-a77d-b1a9e84904ea'::uuid),
                ('88cd0c58-026f-49e0-a6a8-b978b04562ab'::uuid),
                ('079504a1-7b31-4505-bf70-ca3a6ced0bbe'::uuid),
                ('6ab2174b-9321-42fe-9cd7-4d2b47f7ac92'::uuid),
                ('37ac8124-0133-4728-85cb-c364462eda0b'::uuid),
                ('ecbf4e5b-78fa-4158-a73f-3bf166c497d2'::uuid)) AS f(id)
            WHERE c.type = 'maturity_assessment';
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Nothing to undo. The quarterly module ids these collections held were never meaningful for a maturity
        // assessment, and deleting the section rows would leave published collections with no forms at all.
    }
}