using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DrinkWater.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AllowNegativeWaterLogAmount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_WaterLogs_Amount",
                table: "WaterLogs");

            migrationBuilder.AddCheckConstraint(
                name: "CK_WaterLogs_Amount",
                table: "WaterLogs",
                sql: "\"AmountMl\" <> 0 AND \"AmountMl\" BETWEEN -20000 AND 20000");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_WaterLogs_Amount",
                table: "WaterLogs");

            migrationBuilder.AddCheckConstraint(
                name: "CK_WaterLogs_Amount",
                table: "WaterLogs",
                sql: "\"AmountMl\" > 0");
        }
    }
}
