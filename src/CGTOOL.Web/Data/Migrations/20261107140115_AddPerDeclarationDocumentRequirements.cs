using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CGTOOL.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPerDeclarationDocumentRequirements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DocumentTypeRequirements",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DocumentTypeId = table.Column<int>(type: "int", nullable: false),
                    Declaration = table.Column<int>(type: "int", nullable: false),
                    Requirement = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentTypeRequirements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DocumentTypeRequirements_DocumentTypes_DocumentTypeId",
                        column: x => x.DocumentTypeId,
                        principalTable: "DocumentTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RelatedPartyCoiDeclarationDocuments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RelatedPartyCoiDeclarationId = table.Column<int>(type: "int", nullable: false),
                    DocumentTypeId = table.Column<int>(type: "int", nullable: false),
                    FilePath = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: true),
                    OriginalFileName = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: true),
                    DocumentNumber = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ExpiryDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RelatedPartyCoiDeclarationDocuments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RelatedPartyCoiDeclarationDocuments_DocumentTypes_DocumentTypeId",
                        column: x => x.DocumentTypeId,
                        principalTable: "DocumentTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RelatedPartyCoiDeclarationDocuments_RelatedPartyCoiDeclarations_RelatedPartyCoiDeclarationId",
                        column: x => x.RelatedPartyCoiDeclarationId,
                        principalTable: "RelatedPartyCoiDeclarations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DocumentTypeRequirements_DocumentTypeId_Declaration",
                table: "DocumentTypeRequirements",
                columns: new[] { "DocumentTypeId", "Declaration" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RelatedPartyCoiDeclarationDocuments_DocumentTypeId",
                table: "RelatedPartyCoiDeclarationDocuments",
                column: "DocumentTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_RelatedPartyCoiDeclarationDocuments_RelatedPartyCoiDeclarationId",
                table: "RelatedPartyCoiDeclarationDocuments",
                column: "RelatedPartyCoiDeclarationId");

            /*
                Carry the single IsMandatory flag across to both declarations before dropping it.

                Each document keeps exactly the standing it had: required everywhere becomes
                required on both declarations, optional becomes optional on both. Nobody's
                configuration changes on upgrade -- the two sets only diverge once an
                administrator makes them diverge, which is the whole point of the tabs.

                DeclarationType: 0 InsiderTrading, 1 ConflictOfInterest.
                DocumentRequirement: 1 Optional, 2 Required.

                Guarded with NOT EXISTS so a re-run cannot double-insert, and so a database that
                already has rows (a hand-seeded test one) is left alone.
            */
            migrationBuilder.Sql(@"
                INSERT INTO dbo.DocumentTypeRequirements (DocumentTypeId, Declaration, Requirement)
                SELECT t.Id, d.Declaration, CASE WHEN t.IsMandatory = 1 THEN 2 ELSE 1 END
                FROM dbo.DocumentTypes AS t
                CROSS JOIN (SELECT 0 AS Declaration UNION ALL SELECT 1) AS d
                WHERE NOT EXISTS (
                    SELECT 1 FROM dbo.DocumentTypeRequirements AS r
                    WHERE r.DocumentTypeId = t.Id AND r.Declaration = d.Declaration);
            ");

            // Only now, with everything it said preserved in rows.
            migrationBuilder.DropColumn(
                name: "IsMandatory",
                table: "DocumentTypes");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DocumentTypeRequirements");

            migrationBuilder.DropTable(
                name: "RelatedPartyCoiDeclarationDocuments");

            migrationBuilder.AddColumn<bool>(
                name: "IsMandatory",
                table: "DocumentTypes",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }
    }
}
