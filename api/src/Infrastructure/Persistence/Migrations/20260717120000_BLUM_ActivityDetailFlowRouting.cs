using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

// Originally converged the three mutually exclusive detail screens in place (People and roles -> "Role details";
// Education and Help -> "People and issues" for every type; everything else -> "Activity details"), inserting the
// Education section and its questions and deactivating the replaced rows. Emptied for BLUM-7081: migrations no
// longer insert or update seed content. SeedFormDesigner already produces exactly this routing —
// SeedPeopleAndIssuesSection gives Education and Help a category-wide "People and issues" section with
// HELP_RELATED_ISSUES required and no "Activity details" section at all — and
// 20260721120000_BLUM_7081_ReseedActivityFormSchema clears the schema tables so the seeder rebuilds it.
//
// Its NA_SHARED deactivation is deliberately not carried over: that is the BLUM-7081 regression being fixed.
//
// The class and its migration id stay so __EFMigrationsHistory on deployed environments still resolves.
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260717120000_BLUM_ActivityDetailFlowRouting")]
public partial class BLUM_ActivityDetailFlowRouting : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
    }
}