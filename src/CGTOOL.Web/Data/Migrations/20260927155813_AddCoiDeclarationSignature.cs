using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CGTOOL.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCoiDeclarationSignature : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SignaturePath",
                table: "RelatedPartyCoiDeclarations",
                type: "nvarchar(260)",
                maxLength: 260,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SignedAtUtc",
                table: "RelatedPartyCoiDeclarations",
                type: "datetime2",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SignaturePath",
                table: "RelatedPartyCoiDeclarations");

            migrationBuilder.DropColumn(
                name: "SignedAtUtc",
                table: "RelatedPartyCoiDeclarations");
        }
    }
}
