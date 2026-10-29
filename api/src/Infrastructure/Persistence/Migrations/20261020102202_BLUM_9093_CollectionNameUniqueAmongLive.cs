using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BLUM_9093_CollectionNameUniqueAmongLive : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_collections_name",
                table: "collections");

            migrationBuilder.CreateIndex(
                name: "ix_collections_name",
                table: "collections",
                column: "name",
                unique: true,
                filter: "deleted_at IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_collections_name",
                table: "collections");

            migrationBuilder.CreateIndex(
                name: "ix_collections_name",
                table: "collections",
                column: "name",
                unique: true);
        }
    }
}