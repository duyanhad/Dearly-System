using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DrinkWater.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMessageVoice : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AudioDurationMs",
                table: "Messages",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AudioUrl",
                table: "Messages",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AudioDurationMs",
                table: "Messages");

            migrationBuilder.DropColumn(
                name: "AudioUrl",
                table: "Messages");
        }
    }
}
