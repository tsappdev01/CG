using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CGTOOL.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddNatureOfInterest : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "NatureOfInterest",
                table: "OwnedCompanies",
                type: "nvarchar(400)",
                maxLength: 400,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NatureOfInterest",
                table: "FamilyMemberHoldings",
                type: "nvarchar(400)",
                maxLength: 400,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NatureOfInterest",
                table: "CoiConflictEntries",
                type: "nvarchar(400)",
                maxLength: 400,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "NatureOfInterest",
                table: "OwnedCompanies");

            migrationBuilder.DropColumn(
                name: "NatureOfInterest",
                table: "FamilyMemberHoldings");

            migrationBuilder.DropColumn(
                name: "NatureOfInterest",
                table: "CoiConflictEntries");
        }
    }
}
