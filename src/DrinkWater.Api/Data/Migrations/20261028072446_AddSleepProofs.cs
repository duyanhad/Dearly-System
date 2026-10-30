using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DrinkWater.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSleepProofs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Note",
                table: "SleepLogs",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "PhotoUrl",
                table: "SleepLogs",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RejectReason",
                table: "SleepLogs",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReviewedAt",
                table: "SleepLogs",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "SleepLogs",
                type: "text",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Note",
                table: "SleepLogs");

            migrationBuilder.DropColumn(
                name: "PhotoUrl",
                table: "SleepLogs");

            migrationBuilder.DropColumn(
                name: "RejectReason",
                table: "SleepLogs");

            migrationBuilder.DropColumn(
                name: "ReviewedAt",
                table: "SleepLogs");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "SleepLogs");
        }
    }
}
