using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DrinkWater.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddWaterReminders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "ReminderEnabled",
                table: "Users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "ReminderIntervalMinutes",
                table: "Users",
                type: "integer",
                nullable: false,
                defaultValue: 120);

            migrationBuilder.AddColumn<int>(
                name: "ReminderMl",
                table: "Users",
                type: "integer",
                nullable: false,
                defaultValue: 200);

            migrationBuilder.AddColumn<string>(
                name: "ReminderStyle",
                table: "Users",
                type: "text",
                nullable: false,
                defaultValue: "normal");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ReminderEnabled",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "ReminderIntervalMinutes",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "ReminderMl",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "ReminderStyle",
                table: "Users");
        }
    }
}
