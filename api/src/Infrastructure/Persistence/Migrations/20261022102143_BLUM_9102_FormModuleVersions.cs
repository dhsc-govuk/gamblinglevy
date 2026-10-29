using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BLUM_9102_FormModuleVersions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "form_module_versions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    form_module_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version_number = table.Column<int>(type: "integer", nullable: false),
                    definition = table.Column<string>(type: "jsonb", nullable: false),
                    content_hash = table.Column<string>(type: "varchar(64)", nullable: false),
                    published_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    published_by = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_form_module_versions", x => x.id);
                    table.ForeignKey(
                        name: "fk_form_module_versions_form_modules_form_module_id",
                        column: x => x.form_module_id,
                        principalTable: "form_modules",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_form_module_versions_form_module_id_version_number",
                table: "form_module_versions",
                columns: new[] { "form_module_id", "version_number" },
                unique: true);

            // BLUM-9102: a version is a fixed record of what was published, so the database refuses to change or
            // remove one whatever the caller. Row triggers do not fire on TRUNCATE, so the test-data reset
            // (DataResetService) still empties the table.
            migrationBuilder.Sql(
                """
                CREATE FUNCTION reject_form_module_version_change() RETURNS trigger
                LANGUAGE plpgsql AS $$
                BEGIN
                    RAISE EXCEPTION 'form_module_versions rows cannot be changed or deleted (version %)', OLD.id
                        USING ERRCODE = 'restrict_violation';
                END;
                $$;

                CREATE TRIGGER form_module_versions_immutable
                    BEFORE UPDATE OR DELETE ON form_module_versions
                    FOR EACH ROW EXECUTE FUNCTION reject_form_module_version_change();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP TRIGGER IF EXISTS form_module_versions_immutable ON form_module_versions;
                DROP FUNCTION IF EXISTS reject_form_module_version_change();
                """);

            migrationBuilder.DropTable(
                name: "form_module_versions");
        }
    }
}