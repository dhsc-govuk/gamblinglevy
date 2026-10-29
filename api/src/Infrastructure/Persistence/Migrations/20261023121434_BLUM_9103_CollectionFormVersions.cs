using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BLUM_9103_CollectionFormVersions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "form_module_version_id",
                table: "collection_forms",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_version_chosen",
                table: "collection_forms",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "ix_collection_forms_form_module_version_id",
                table: "collection_forms",
                column: "form_module_version_id");

            migrationBuilder.AddForeignKey(
                name: "fk_collection_forms_form_module_versions_form_module_version_id",
                table: "collection_forms",
                column: "form_module_version_id",
                principalTable: "form_module_versions",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_collection_forms_form_module_versions_form_module_version_id",
                table: "collection_forms");

            migrationBuilder.DropIndex(
                name: "ix_collection_forms_form_module_version_id",
                table: "collection_forms");

            migrationBuilder.DropColumn(
                name: "form_module_version_id",
                table: "collection_forms");

            migrationBuilder.DropColumn(
                name: "is_version_chosen",
                table: "collection_forms");
        }
    }
}