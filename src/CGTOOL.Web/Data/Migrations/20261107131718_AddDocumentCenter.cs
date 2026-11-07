using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CGTOOL.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDocumentCenter : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DocumentTypeId",
                table: "MemberDocuments",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "DocumentTypes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    IsMandatory = table.Column<bool>(type: "bit", nullable: false),
                    BuiltInKind = table.Column<int>(type: "int", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentTypes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "InsiderDeclarationDocuments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    InsiderDeclarationId = table.Column<int>(type: "int", nullable: false),
                    DocumentTypeId = table.Column<int>(type: "int", nullable: false),
                    FilePath = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: true),
                    OriginalFileName = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: true),
                    DocumentNumber = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ExpiryDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InsiderDeclarationDocuments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InsiderDeclarationDocuments_DocumentTypes_DocumentTypeId",
                        column: x => x.DocumentTypeId,
                        principalTable: "DocumentTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InsiderDeclarationDocuments_InsiderDeclarations_InsiderDeclarationId",
                        column: x => x.InsiderDeclarationId,
                        principalTable: "InsiderDeclarations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MemberDocuments_DocumentTypeId",
                table: "MemberDocuments",
                column: "DocumentTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentTypes_BuiltInKind",
                table: "DocumentTypes",
                column: "BuiltInKind",
                unique: true,
                filter: "[BuiltInKind] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentTypes_Name",
                table: "DocumentTypes",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InsiderDeclarationDocuments_DocumentTypeId",
                table: "InsiderDeclarationDocuments",
                column: "DocumentTypeId");

            /*
                The four built-in rows, seeded here rather than left to a script in scripts/.

                Reference data is normally data, maintained by script, and the names and the
                Required/Optional flags below are exactly that -- an administrator changes them in
                the Document Centre afterwards and this migration never touches them again. But the
                rows themselves are not optional: My Register and the declaration render their
                document cards from this table, so a deployment that migrated and had not yet run a
                seed script would show a person no Emirates ID card, no passport card, and a
                declaration that could be completed without either. Shipping the rows with the
                schema is the difference between a setting someone has not got round to and an
                application that silently stopped asking for a passport.

                Matched on BuiltInKind, not on Name: by the second run the names may have been
                changed, and matching on them would seed a duplicate.

                The numbers are MemberDocumentKind -- 0 EmiratesId, 1 Passport, 2 TradeLicence,
                3 Other.
            */
            migrationBuilder.Sql(@"
                IF NOT EXISTS (SELECT 1 FROM dbo.DocumentTypes WHERE BuiltInKind = 0)
                    INSERT INTO dbo.DocumentTypes (Name, Description, IsMandatory, BuiltInKind, IsActive, SortOrder)
                    VALUES (N'Emirates ID', N'Number, name on the ID, expiry and date of birth.', 1, 0, 1, 10);

                IF NOT EXISTS (SELECT 1 FROM dbo.DocumentTypes WHERE BuiltInKind = 1)
                    INSERT INTO dbo.DocumentTypes (Name, Description, IsMandatory, BuiltInKind, IsActive, SortOrder)
                    VALUES (N'Passport', N'Number, nationality, issue and expiry dates.', 1, 1, 1, 20);

                IF NOT EXISTS (SELECT 1 FROM dbo.DocumentTypes WHERE BuiltInKind = 2)
                    INSERT INTO dbo.DocumentTypes (Name, Description, IsMandatory, BuiltInKind, IsActive, SortOrder)
                    VALUES (N'Trade License', N'Licence number, legal name and expiry.', 0, 2, 1, 30);

                IF NOT EXISTS (SELECT 1 FROM dbo.DocumentTypes WHERE BuiltInKind = 3)
                    INSERT INTO dbo.DocumentTypes (Name, Description, IsMandatory, BuiltInKind, IsActive, SortOrder)
                    VALUES (N'Other Documents', N'Anything else worth keeping on file. Name it yourself; these are not read automatically.', 0, 3, 1, 40);
            ");

            migrationBuilder.CreateIndex(
                name: "IX_InsiderDeclarationDocuments_InsiderDeclarationId",
                table: "InsiderDeclarationDocuments",
                column: "InsiderDeclarationId");

            migrationBuilder.AddForeignKey(
                name: "FK_MemberDocuments_DocumentTypes_DocumentTypeId",
                table: "MemberDocuments",
                column: "DocumentTypeId",
                principalTable: "DocumentTypes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_MemberDocuments_DocumentTypes_DocumentTypeId",
                table: "MemberDocuments");

            migrationBuilder.DropTable(
                name: "InsiderDeclarationDocuments");

            migrationBuilder.DropTable(
                name: "DocumentTypes");

            migrationBuilder.DropIndex(
                name: "IX_MemberDocuments_DocumentTypeId",
                table: "MemberDocuments");

            migrationBuilder.DropColumn(
                name: "DocumentTypeId",
                table: "MemberDocuments");
        }
    }
}
