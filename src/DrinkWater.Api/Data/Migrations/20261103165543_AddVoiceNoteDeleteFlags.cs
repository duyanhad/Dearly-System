using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DrinkWater.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddVoiceNoteDeleteFlags : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ReceiverDeletedAtUtc",
                table: "VoiceNotes",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SenderDeletedAtUtc",
                table: "VoiceNotes",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ReceiverDeletedAtUtc",
                table: "VoiceNotes");

            migrationBuilder.DropColumn(
                name: "SenderDeletedAtUtc",
                table: "VoiceNotes");
        }
    }
}
