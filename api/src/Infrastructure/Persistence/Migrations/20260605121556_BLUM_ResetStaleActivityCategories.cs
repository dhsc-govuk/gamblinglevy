using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BLUM_ResetStaleActivityCategories : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The activity taxonomy was fully redesigned in BLUM_ActivityRedesign. Environments seeded before
            // the redesign still hold the obsolete 'Education'/'Treatment' categories, which made
            // SeedActivityReferenceData skip seeding the new taxonomy and SeedFormDesigner crash on
            // .First("People and roles"). Remove the stale rows by name so the seeder recreates the current
            // 6-category taxonomy on next startup. No-op on fresh DBs and on DBs already holding the new taxonomy.
            migrationBuilder.Sql(
                "DELETE FROM activity_tasks WHERE category_id IN (SELECT id FROM categories WHERE name IN ('Education', 'Treatment'));");
            migrationBuilder.Sql(
                "DELETE FROM categories WHERE name IN ('Education', 'Treatment');");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Reference-data cleanup is not reversible.
        }
    }
}