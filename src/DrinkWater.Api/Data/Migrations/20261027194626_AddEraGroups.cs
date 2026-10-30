using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DrinkWater.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddEraGroups : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "GroupId",
                table: "Eras",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "EraGroups",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    Slug = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Color = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Order = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EraGroups", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Eras_GroupId",
                table: "Eras",
                column: "GroupId");

            migrationBuilder.CreateIndex(
                name: "IX_EraGroups_Order",
                table: "EraGroups",
                column: "Order");

            migrationBuilder.CreateIndex(
                name: "IX_EraGroups_Slug",
                table: "EraGroups",
                column: "Slug",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Eras_EraGroups_GroupId",
                table: "Eras",
                column: "GroupId",
                principalTable: "EraGroups",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Eras_EraGroups_GroupId",
                table: "Eras");

            migrationBuilder.DropTable(
                name: "EraGroups");

            migrationBuilder.DropIndex(
                name: "IX_Eras_GroupId",
                table: "Eras");

            migrationBuilder.DropColumn(
                name: "GroupId",
                table: "Eras");
        }
    }
}
