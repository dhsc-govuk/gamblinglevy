using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BLUM_7620_AddQuestionErrorAndLengthColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "error_message",
                table: "form_questions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "max_length",
                table: "form_questions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "max_length_error_message",
                table: "form_questions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "error_message",
                table: "dc_form_questions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "max_length",
                table: "dc_form_questions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "max_length_error_message",
                table: "dc_form_questions",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "error_message",
                table: "form_questions");

            migrationBuilder.DropColumn(
                name: "max_length",
                table: "form_questions");

            migrationBuilder.DropColumn(
                name: "max_length_error_message",
                table: "form_questions");

            migrationBuilder.DropColumn(
                name: "error_message",
                table: "dc_form_questions");

            migrationBuilder.DropColumn(
                name: "max_length",
                table: "dc_form_questions");

            migrationBuilder.DropColumn(
                name: "max_length_error_message",
                table: "dc_form_questions");
        }
    }
}