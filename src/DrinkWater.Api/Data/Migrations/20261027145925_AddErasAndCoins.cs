using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DrinkWater.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddErasAndCoins : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Coins",
                table: "Users",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "CoinsAwarded",
                table: "DailySummaries",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "Eras",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Order = table.Column<int>(type: "integer", nullable: false),
                    Title = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Subtitle = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    BgFrom = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    BgTo = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    UnlockPrice = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Eras", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EraLevels",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EraId = table.Column<Guid>(type: "uuid", nullable: false),
                    Level = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Description = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Artifact = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Price = table.Column<int>(type: "integer", nullable: false),
                    ImageUrl = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EraLevels", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EraLevels_Eras_EraId",
                        column: x => x.EraId,
                        principalTable: "Eras",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserEraProgresses",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    EraId = table.Column<Guid>(type: "uuid", nullable: false),
                    UnlockedLevel = table.Column<int>(type: "integer", nullable: false),
                    UnlockedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserEraProgresses", x => new { x.UserId, x.EraId });
                    table.ForeignKey(
                        name: "FK_UserEraProgresses_Eras_EraId",
                        column: x => x.EraId,
                        principalTable: "Eras",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserEraProgresses_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EraLevels_EraId_Level",
                table: "EraLevels",
                columns: new[] { "EraId", "Level" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Eras_Order",
                table: "Eras",
                column: "Order");

            migrationBuilder.CreateIndex(
                name: "IX_UserEraProgresses_EraId",
                table: "UserEraProgresses",
                column: "EraId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EraLevels");

            migrationBuilder.DropTable(
                name: "UserEraProgresses");

            migrationBuilder.DropTable(
                name: "Eras");

            migrationBuilder.DropColumn(
                name: "Coins",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "CoinsAwarded",
                table: "DailySummaries");
        }
    }
}
