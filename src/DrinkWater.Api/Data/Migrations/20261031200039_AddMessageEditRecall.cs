using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DrinkWater.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMessageEditRecall : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "EditedAt",
                table: "Messages",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RecalledAt",
                table: "Messages",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EditedAt",
                table: "Messages");

            migrationBuilder.DropColumn(
                name: "RecalledAt",
                table: "Messages");
        }
    }
}
