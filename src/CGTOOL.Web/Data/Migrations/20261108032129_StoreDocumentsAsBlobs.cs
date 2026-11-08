using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CGTOOL.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class StoreDocumentsAsBlobs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "BlobId",
                table: "ShareholderRegisterUploads",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BlobId",
                table: "RelatedPartyTransactionDocuments",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SignatureBlobId",
                table: "RelatedPartyCoiDeclarations",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BlobId",
                table: "RelatedPartyCoiDeclarationDocuments",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BlobId",
                table: "PolicyDocumentVersions",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MoaBlobId",
                table: "OwnedCompanies",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PoaBlobId",
                table: "OwnedCompanies",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TradeLicenseBlobId",
                table: "OwnedCompanies",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BlobId",
                table: "MemberDocuments",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "EmiratesIdBlobId",
                table: "InsiderDeclarations",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OtherDocumentBlobId",
                table: "InsiderDeclarations",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PassportBlobId",
                table: "InsiderDeclarations",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TradeLicenceBlobId",
                table: "InsiderDeclarations",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BlobId",
                table: "InsiderDeclarationDocuments",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "EmiratesIdBlobId",
                table: "FamilyMembers",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PassportBlobId",
                table: "FamilyMembers",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TradeLicenceBlobId",
                table: "FamilyMembers",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TradeLicenceBlobId",
                table: "FamilyMemberHoldings",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LogoBlobId",
                table: "Companies",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BlobId",
                table: "CoiTradeLicenseDocuments",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ProfilePictureBlobId",
                table: "AspNetUsers",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "DocumentBlobs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FileName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    ContentProtected = table.Column<byte[]>(type: "varbinary(max)", nullable: false),
                    ProtectionScheme = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ContentSha256 = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    IpAddress = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    UploadedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentBlobs", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DocumentBlobs");

            migrationBuilder.DropColumn(
                name: "BlobId",
                table: "ShareholderRegisterUploads");

            migrationBuilder.DropColumn(
                name: "BlobId",
                table: "RelatedPartyTransactionDocuments");

            migrationBuilder.DropColumn(
                name: "SignatureBlobId",
                table: "RelatedPartyCoiDeclarations");

            migrationBuilder.DropColumn(
                name: "BlobId",
                table: "RelatedPartyCoiDeclarationDocuments");

            migrationBuilder.DropColumn(
                name: "BlobId",
                table: "PolicyDocumentVersions");

            migrationBuilder.DropColumn(
                name: "MoaBlobId",
                table: "OwnedCompanies");

            migrationBuilder.DropColumn(
                name: "PoaBlobId",
                table: "OwnedCompanies");

            migrationBuilder.DropColumn(
                name: "TradeLicenseBlobId",
                table: "OwnedCompanies");

            migrationBuilder.DropColumn(
                name: "BlobId",
                table: "MemberDocuments");

            migrationBuilder.DropColumn(
                name: "EmiratesIdBlobId",
                table: "InsiderDeclarations");

            migrationBuilder.DropColumn(
                name: "OtherDocumentBlobId",
                table: "InsiderDeclarations");

            migrationBuilder.DropColumn(
                name: "PassportBlobId",
                table: "InsiderDeclarations");

            migrationBuilder.DropColumn(
                name: "TradeLicenceBlobId",
                table: "InsiderDeclarations");

            migrationBuilder.DropColumn(
                name: "BlobId",
                table: "InsiderDeclarationDocuments");

            migrationBuilder.DropColumn(
                name: "EmiratesIdBlobId",
                table: "FamilyMembers");

            migrationBuilder.DropColumn(
                name: "PassportBlobId",
                table: "FamilyMembers");

            migrationBuilder.DropColumn(
                name: "TradeLicenceBlobId",
                table: "FamilyMembers");

            migrationBuilder.DropColumn(
                name: "TradeLicenceBlobId",
                table: "FamilyMemberHoldings");

            migrationBuilder.DropColumn(
                name: "LogoBlobId",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "BlobId",
                table: "CoiTradeLicenseDocuments");

            migrationBuilder.DropColumn(
                name: "ProfilePictureBlobId",
                table: "AspNetUsers");
        }
    }
}
