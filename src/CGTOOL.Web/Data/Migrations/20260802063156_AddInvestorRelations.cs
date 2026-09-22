using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CGTOOL.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddInvestorRelations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ShareholderRegisterUploads",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AsOnDate = table.Column<DateOnly>(type: "date", nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: false),
                    RecordCount = table.Column<int>(type: "int", nullable: false),
                    UploadedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UploadedByName = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShareholderRegisterUploads", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ShareTradingUploads",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FileName = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: false),
                    RecordCount = table.Column<int>(type: "int", nullable: false),
                    UploadedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UploadedByName = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShareTradingUploads", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ShareholderRecords",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ShareholderRegisterUploadId = table.Column<int>(type: "int", nullable: false),
                    SerialNo = table.Column<int>(type: "int", nullable: true),
                    Nin = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    CdsUpdated = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    EnglishName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    LifeStatus = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    ClientType = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    PassportNo = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    FamilyId = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    NationalId = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    VisaNo = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    CommercialLicenseNo = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    TradeRegistrationNo = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    Citizenship = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    CitizenshipDescription = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    PoBox = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    City = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    CountryCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    CountryName = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    Address1 = table.Column<string>(type: "nvarchar(240)", maxLength: 240, nullable: true),
                    Address2 = table.Column<string>(type: "nvarchar(240)", maxLength: 240, nullable: true),
                    Address3 = table.Column<string>(type: "nvarchar(240)", maxLength: 240, nullable: true),
                    Phone1 = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    Phone2 = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    Fax = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    Email = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: true),
                    Qty = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    QtyPercent = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: false),
                    Frozen = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    LastTransDate = table.Column<DateOnly>(type: "date", nullable: true),
                    PaymentPreference = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShareholderRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ShareholderRecords_ShareholderRegisterUploads_ShareholderRegisterUploadId",
                        column: x => x.ShareholderRegisterUploadId,
                        principalTable: "ShareholderRegisterUploads",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ShareTradingRecords",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ShareTradingUploadId = table.Column<int>(type: "int", nullable: false),
                    ReportDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Symbol = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Nin = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    InvestorName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ClientType = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    Nationality = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    PreviousOwnQty = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    CurrentOwnQty = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    OwnedQtyChange = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShareTradingRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ShareTradingRecords_ShareTradingUploads_ShareTradingUploadId",
                        column: x => x.ShareTradingUploadId,
                        principalTable: "ShareTradingUploads",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ShareholderRecords_ShareholderRegisterUploadId_Nin",
                table: "ShareholderRecords",
                columns: new[] { "ShareholderRegisterUploadId", "Nin" });

            migrationBuilder.CreateIndex(
                name: "IX_ShareholderRegisterUploads_AsOnDate",
                table: "ShareholderRegisterUploads",
                column: "AsOnDate");

            migrationBuilder.CreateIndex(
                name: "IX_ShareTradingRecords_ReportDate_Nin",
                table: "ShareTradingRecords",
                columns: new[] { "ReportDate", "Nin" });

            migrationBuilder.CreateIndex(
                name: "IX_ShareTradingRecords_ShareTradingUploadId",
                table: "ShareTradingRecords",
                column: "ShareTradingUploadId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ShareholderRecords");

            migrationBuilder.DropTable(
                name: "ShareTradingRecords");

            migrationBuilder.DropTable(
                name: "ShareholderRegisterUploads");

            migrationBuilder.DropTable(
                name: "ShareTradingUploads");
        }
    }
}
