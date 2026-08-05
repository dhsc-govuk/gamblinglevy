using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BLUM_6008_LaAccountManagement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_audit_logs_target_user_id",
                table: "audit_logs");

            migrationBuilder.DropColumn(
                name: "is_active",
                table: "organisations");

            migrationBuilder.RenameColumn(
                name: "target_user_id",
                table: "audit_logs",
                newName: "target_id");

            migrationBuilder.AddColumn<DateTime>(
                name: "created_at",
                table: "organisations",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<string>(
                name: "onboarding_status",
                table: "organisations",
                type: "varchar(32)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ons_code",
                table: "organisations",
                type: "varchar(64)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "region",
                table: "organisations",
                type: "varchar(64)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "type",
                table: "organisations",
                type: "varchar(16)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "updated_at",
                table: "organisations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "vendor_id",
                table: "organisations",
                type: "varchar(64)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "target_type",
                table: "audit_logs",
                type: "varchar(50)",
                nullable: false,
                defaultValue: "");

            // Backfill existing audit log rows (User-targeted before this migration).
            migrationBuilder.Sql("UPDATE audit_logs SET target_type = 'User' WHERE target_type = '';");

            migrationBuilder.CreateTable(
                name: "funding_windows",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    financial_year = table.Column<string>(type: "varchar(16)", nullable: false),
                    window_label = table.Column<string>(type: "varchar(64)", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_funding_windows", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "org_funding_allocations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organisation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    funding_window_id = table.Column<Guid>(type: "uuid", nullable: false),
                    allocated_amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    amended_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    amended_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_org_funding_allocations", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "org_neighbours",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organisation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    neighbour_org_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source = table.Column<string>(type: "varchar(32)", nullable: false),
                    reference_tool = table.Column<string>(type: "varchar(128)", nullable: true),
                    imported_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    imported_by = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_org_neighbours", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_organisations_ons_code",
                table: "organisations",
                column: "ons_code",
                unique: true,
                filter: "ons_code IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_audit_logs_target_type_target_id",
                table: "audit_logs",
                columns: new[] { "target_type", "target_id" });

            migrationBuilder.CreateIndex(
                name: "ix_funding_windows_financial_year",
                table: "funding_windows",
                column: "financial_year",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_org_funding_allocations_organisation_id",
                table: "org_funding_allocations",
                column: "organisation_id");

            migrationBuilder.CreateIndex(
                name: "ix_org_funding_allocations_organisation_id_funding_window_id",
                table: "org_funding_allocations",
                columns: new[] { "organisation_id", "funding_window_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_org_neighbours_organisation_id",
                table: "org_neighbours",
                column: "organisation_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "funding_windows");

            migrationBuilder.DropTable(
                name: "org_funding_allocations");

            migrationBuilder.DropTable(
                name: "org_neighbours");

            migrationBuilder.DropIndex(
                name: "ix_organisations_ons_code",
                table: "organisations");

            migrationBuilder.DropIndex(
                name: "ix_audit_logs_target_type_target_id",
                table: "audit_logs");

            migrationBuilder.DropColumn(
                name: "created_at",
                table: "organisations");

            migrationBuilder.DropColumn(
                name: "onboarding_status",
                table: "organisations");

            migrationBuilder.DropColumn(
                name: "ons_code",
                table: "organisations");

            migrationBuilder.DropColumn(
                name: "region",
                table: "organisations");

            migrationBuilder.DropColumn(
                name: "type",
                table: "organisations");

            migrationBuilder.DropColumn(
                name: "updated_at",
                table: "organisations");

            migrationBuilder.DropColumn(
                name: "vendor_id",
                table: "organisations");

            migrationBuilder.DropColumn(
                name: "target_type",
                table: "audit_logs");

            migrationBuilder.RenameColumn(
                name: "target_id",
                table: "audit_logs",
                newName: "target_user_id");

            migrationBuilder.AddColumn<bool>(
                name: "is_active",
                table: "organisations",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "ix_audit_logs_target_user_id",
                table: "audit_logs",
                column: "target_user_id");
        }
    }
}