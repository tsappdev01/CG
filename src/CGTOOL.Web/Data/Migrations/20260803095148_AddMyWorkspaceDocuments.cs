using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CGTOOL.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMyWorkspaceDocuments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "MoaPath",
                table: "OwnedCompanies",
                type: "nvarchar(260)",
                maxLength: 260,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PoaPath",
                table: "OwnedCompanies",
                type: "nvarchar(260)",
                maxLength: 260,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TradeLicensePath",
                table: "OwnedCompanies",
                type: "nvarchar(260)",
                maxLength: 260,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EmiratesIdPath",
                table: "FamilyMembers",
                type: "nvarchar(260)",
                maxLength: 260,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PassportPath",
                table: "FamilyMembers",
                type: "nvarchar(260)",
                maxLength: 260,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MoaPath",
                table: "OwnedCompanies");

            migrationBuilder.DropColumn(
                name: "PoaPath",
                table: "OwnedCompanies");

            migrationBuilder.DropColumn(
                name: "TradeLicensePath",
                table: "OwnedCompanies");

            migrationBuilder.DropColumn(
                name: "EmiratesIdPath",
                table: "FamilyMembers");

            migrationBuilder.DropColumn(
                name: "PassportPath",
                table: "FamilyMembers");
        }
    }
}
