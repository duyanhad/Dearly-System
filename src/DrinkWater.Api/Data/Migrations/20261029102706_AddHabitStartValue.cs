using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DrinkWater.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddHabitStartValue : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "StartDate",
                table: "HabitSettings",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "StartValue",
                table: "HabitSettings",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "StartDate",
                table: "HabitSettings");

            migrationBuilder.DropColumn(
                name: "StartValue",
                table: "HabitSettings");
        }
    }
}
