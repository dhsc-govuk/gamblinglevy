using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BLUM_7041_RequireActivityDetailsQuestions : Migration
    {
        // Originally set is_required = true on the 20 seeded "Activity details" question codes in place, because
        // SeedFormDesigner is guarded by "if (FormSections.Any()) return" and would not revisit an already-seeded
        // environment. Emptied for BLUM-7081: migrations no longer insert or update seed content — the flags live
        // in SeedFormDesigner (every question is seeded isRequired: true), and
        // 20260721120000_BLUM_7081_ReseedActivityFormSchema clears the schema tables so the seeder rebuilds them.
        //
        // The class and its migration id stay so __EFMigrationsHistory on deployed environments still resolves.

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}