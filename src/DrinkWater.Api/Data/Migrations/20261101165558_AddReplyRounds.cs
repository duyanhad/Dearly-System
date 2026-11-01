using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DrinkWater.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddReplyRounds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "RootId",
                table: "VoiceNotes",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Round",
                table: "VoiceNotes",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ReplyRootId",
                table: "Notifications",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ReplyRound",
                table: "Notifications",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RootId",
                table: "VoiceNotes");

            migrationBuilder.DropColumn(
                name: "Round",
                table: "VoiceNotes");

            migrationBuilder.DropColumn(
                name: "ReplyRootId",
                table: "Notifications");

            migrationBuilder.DropColumn(
                name: "ReplyRound",
                table: "Notifications");
        }
    }
}
