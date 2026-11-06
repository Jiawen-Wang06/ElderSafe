using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ElderSafe.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddEmergencyContactPhoneNumber : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "EmergencyContactPhoneNumber",
                table: "Residents",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EmergencyContactPhoneNumber",
                table: "Residents");
        }
    }
}
