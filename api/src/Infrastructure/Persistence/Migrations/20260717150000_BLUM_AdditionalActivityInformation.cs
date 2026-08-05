using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

// Originally inserted the "Additional information" section for the five non-People-and-roles categories and moved
// each {prefix}_PLAN question into it. Emptied for BLUM-7081: migrations no longer insert or update seed content.
// SeedAdditionalInformationSection already creates that section (display order 1) holding the plan question, and
// 20260721120000_BLUM_7081_ReseedActivityFormSchema clears the schema tables so the seeder rebuilds it.
//
// The class and its migration id stay so __EFMigrationsHistory on deployed environments still resolves.
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260717150000_BLUM_AdditionalActivityInformation")]
public partial class BLUM_AdditionalActivityInformation : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
    }
}