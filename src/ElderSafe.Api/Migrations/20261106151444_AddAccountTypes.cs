using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ElderSafe.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddAccountTypes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "AccountId",
                table: "Residents",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AccountType",
                table: "AspNetUsers",
                type: "TEXT",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_Residents_AccountId",
                table: "Residents",
                column: "AccountId",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Residents_AspNetUsers_AccountId",
                table: "Residents",
                column: "AccountId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Residents_AspNetUsers_AccountId",
                table: "Residents");

            migrationBuilder.DropIndex(
                name: "IX_Residents_AccountId",
                table: "Residents");

            migrationBuilder.DropColumn(
                name: "AccountId",
                table: "Residents");

            migrationBuilder.DropColumn(
                name: "AccountType",
                table: "AspNetUsers");
        }
    }
}
