using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DrinkWater.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAlarmModes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AlarmIntervalMin",
                table: "HabitSettings",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "AlarmMode",
                table: "HabitSettings",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "AlarmRepeatPerRound",
                table: "HabitSettings",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "AlarmRounds",
                table: "HabitSettings",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "LockUntilSettings",
                table: "HabitSettings",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AlarmIntervalMin",
                table: "HabitSettings");

            migrationBuilder.DropColumn(
                name: "AlarmMode",
                table: "HabitSettings");

            migrationBuilder.DropColumn(
                name: "AlarmRepeatPerRound",
                table: "HabitSettings");

            migrationBuilder.DropColumn(
                name: "AlarmRounds",
                table: "HabitSettings");

            migrationBuilder.DropColumn(
                name: "LockUntilSettings",
                table: "HabitSettings");
        }
    }
}
