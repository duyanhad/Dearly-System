using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DrinkWater.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AllowMultipleSleepPerDay : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SleepLogs_UserId_Date",
                table: "SleepLogs");

            migrationBuilder.CreateIndex(
                name: "IX_SleepLogs_UserId_Date",
                table: "SleepLogs",
                columns: new[] { "UserId", "Date" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SleepLogs_UserId_Date",
                table: "SleepLogs");

            migrationBuilder.CreateIndex(
                name: "IX_SleepLogs_UserId_Date",
                table: "SleepLogs",
                columns: new[] { "UserId", "Date" },
                unique: true);
        }
    }
}
