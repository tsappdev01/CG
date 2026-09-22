using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CGTOOL.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddInsiderDeclarationNinHoldersAndDocMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "EmiratesIdExpiryDate",
                table: "InsiderDeclarations",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EmiratesIdNameOnCard",
                table: "InsiderDeclarations",
                type: "nvarchar(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EmiratesIdNumber",
                table: "InsiderDeclarations",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OtherDocumentPath",
                table: "InsiderDeclarations",
                type: "nvarchar(260)",
                maxLength: 260,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PassportExpiryDate",
                table: "InsiderDeclarations",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PassportIssuingCountry",
                table: "InsiderDeclarations",
                type: "nvarchar(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PassportNumber",
                table: "InsiderDeclarations",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "RelativesHaveNin",
                table: "InsiderDeclarations",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "TradeLicenceIssuingAuthority",
                table: "InsiderDeclarations",
                type: "nvarchar(160)",
                maxLength: 160,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TradeLicenceLegalName",
                table: "InsiderDeclarations",
                type: "nvarchar(160)",
                maxLength: 160,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TradeLicenceNumber",
                table: "InsiderDeclarations",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TradeLicencePath",
                table: "InsiderDeclarations",
                type: "nvarchar(260)",
                maxLength: 260,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Additional",
                table: "InsiderDeclarationRelatives",
                type: "nvarchar(400)",
                maxLength: 400,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "InsiderDeclarationNinHolders",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    InsiderDeclarationId = table.Column<int>(type: "int", nullable: false),
                    Relationship = table.Column<int>(type: "int", nullable: false),
                    NameOfShareHolder = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    NinNumber = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Additional = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InsiderDeclarationNinHolders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InsiderDeclarationNinHolders_InsiderDeclarations_InsiderDeclarationId",
                        column: x => x.InsiderDeclarationId,
                        principalTable: "InsiderDeclarations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InsiderDeclarationNinHolders_InsiderDeclarationId",
                table: "InsiderDeclarationNinHolders",
                column: "InsiderDeclarationId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InsiderDeclarationNinHolders");

            migrationBuilder.DropColumn(
                name: "EmiratesIdExpiryDate",
                table: "InsiderDeclarations");

            migrationBuilder.DropColumn(
                name: "EmiratesIdNameOnCard",
                table: "InsiderDeclarations");

            migrationBuilder.DropColumn(
                name: "EmiratesIdNumber",
                table: "InsiderDeclarations");

            migrationBuilder.DropColumn(
                name: "OtherDocumentPath",
                table: "InsiderDeclarations");

            migrationBuilder.DropColumn(
                name: "PassportExpiryDate",
                table: "InsiderDeclarations");

            migrationBuilder.DropColumn(
                name: "PassportIssuingCountry",
                table: "InsiderDeclarations");

            migrationBuilder.DropColumn(
                name: "PassportNumber",
                table: "InsiderDeclarations");

            migrationBuilder.DropColumn(
                name: "RelativesHaveNin",
                table: "InsiderDeclarations");

            migrationBuilder.DropColumn(
                name: "TradeLicenceIssuingAuthority",
                table: "InsiderDeclarations");

            migrationBuilder.DropColumn(
                name: "TradeLicenceLegalName",
                table: "InsiderDeclarations");

            migrationBuilder.DropColumn(
                name: "TradeLicenceNumber",
                table: "InsiderDeclarations");

            migrationBuilder.DropColumn(
                name: "TradeLicencePath",
                table: "InsiderDeclarations");

            migrationBuilder.DropColumn(
                name: "Additional",
                table: "InsiderDeclarationRelatives");
        }
    }
}
