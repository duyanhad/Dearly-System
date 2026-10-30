using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DrinkWater.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddReminderHours : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ReminderEndHour",
                table: "Users",
                type: "integer",
                nullable: false,
                defaultValue: 23);

            migrationBuilder.AddColumn<int>(
                name: "ReminderStartHour",
                table: "Users",
                type: "integer",
                nullable: false,
                defaultValue: 7);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ReminderEndHour",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "ReminderStartHour",
                table: "Users");
        }
    }
}
