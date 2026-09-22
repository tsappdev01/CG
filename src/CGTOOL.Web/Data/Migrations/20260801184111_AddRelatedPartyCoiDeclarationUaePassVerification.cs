using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CGTOOL.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddRelatedPartyCoiDeclarationUaePassVerification : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "UaePassUuid",
                table: "RelatedPartyCoiDeclarations",
                type: "nvarchar(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "UaePassVerifiedAtUtc",
                table: "RelatedPartyCoiDeclarations",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UaePassVerifiedName",
                table: "RelatedPartyCoiDeclarations",
                type: "nvarchar(160)",
                maxLength: 160,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "UaePassUuid",
                table: "RelatedPartyCoiDeclarations");

            migrationBuilder.DropColumn(
                name: "UaePassVerifiedAtUtc",
                table: "RelatedPartyCoiDeclarations");

            migrationBuilder.DropColumn(
                name: "UaePassVerifiedName",
                table: "RelatedPartyCoiDeclarations");
        }
    }
}
