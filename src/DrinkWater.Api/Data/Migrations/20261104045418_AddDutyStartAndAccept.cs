using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DrinkWater.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDutyStartAndAccept : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "AcceptedAtUtc",
                table: "Duties",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastRungAtUtc",
                table: "Duties",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RemindBeforeMin",
                table: "Duties",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "RingCount",
                table: "Duties",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "StartNotifiedAtUtc",
                table: "Duties",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "StartsAtUtc",
                table: "Duties",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AcceptedAtUtc",
                table: "Duties");

            migrationBuilder.DropColumn(
                name: "LastRungAtUtc",
                table: "Duties");

            migrationBuilder.DropColumn(
                name: "RemindBeforeMin",
                table: "Duties");

            migrationBuilder.DropColumn(
                name: "RingCount",
                table: "Duties");

            migrationBuilder.DropColumn(
                name: "StartNotifiedAtUtc",
                table: "Duties");

            migrationBuilder.DropColumn(
                name: "StartsAtUtc",
                table: "Duties");
        }
    }
}
