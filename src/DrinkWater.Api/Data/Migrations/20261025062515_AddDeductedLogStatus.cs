using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DrinkWater.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDeductedLogStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_WaterLogs_Status",
                table: "WaterLogs");

            migrationBuilder.AddCheckConstraint(
                name: "CK_WaterLogs_Status",
                table: "WaterLogs",
                sql: "\"Status\" IN ('Approved','Pending','Rejected','Unverified','Deducted')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_WaterLogs_Status",
                table: "WaterLogs");

            migrationBuilder.AddCheckConstraint(
                name: "CK_WaterLogs_Status",
                table: "WaterLogs",
                sql: "\"Status\" IN ('Approved','Pending','Rejected','Unverified')");
        }
    }
}
