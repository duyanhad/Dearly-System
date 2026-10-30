using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DrinkWater.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class FollowApprovalAndProofReview : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ReviewedAt",
                table: "WaterLogs",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ReviewedBy",
                table: "WaterLogs",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "WaterLogs",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Approved");

            migrationBuilder.AddColumn<Guid>(
                name: "PartnerId",
                table: "Users",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "Follows",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Accepted");

            migrationBuilder.CreateIndex(
                name: "IX_WaterLogs_UserId_Status",
                table: "WaterLogs",
                columns: new[] { "UserId", "Status" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_WaterLogs_Status",
                table: "WaterLogs",
                sql: "\"Status\" IN ('Approved','Pending','Rejected','Unverified')");

            migrationBuilder.CreateIndex(
                name: "IX_Users_PartnerId",
                table: "Users",
                column: "PartnerId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Follows_Status",
                table: "Follows",
                sql: "\"Status\" IN ('Pending','Accepted')");

            migrationBuilder.AddForeignKey(
                name: "FK_Users_Users_PartnerId",
                table: "Users",
                column: "PartnerId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Users_Users_PartnerId",
                table: "Users");

            migrationBuilder.DropIndex(
                name: "IX_WaterLogs_UserId_Status",
                table: "WaterLogs");

            migrationBuilder.DropCheckConstraint(
                name: "CK_WaterLogs_Status",
                table: "WaterLogs");

            migrationBuilder.DropIndex(
                name: "IX_Users_PartnerId",
                table: "Users");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Follows_Status",
                table: "Follows");

            migrationBuilder.DropColumn(
                name: "ReviewedAt",
                table: "WaterLogs");

            migrationBuilder.DropColumn(
                name: "ReviewedBy",
                table: "WaterLogs");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "WaterLogs");

            migrationBuilder.DropColumn(
                name: "PartnerId",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "Follows");
        }
    }
}
