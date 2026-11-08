using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CGTOOL.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMemberOnboarding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MemberOnboardings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MemberId = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    EmiratesIdStatus = table.Column<int>(type: "int", nullable: false),
                    EmiratesIdSource = table.Column<int>(type: "int", nullable: false),
                    EmiratesIdNumber = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    EmiratesIdHolderName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    EmiratesIdHolderNameArabic = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    EmiratesIdNationality = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    EmiratesIdExpiryDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    EmiratesIdConfirmedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    EmiratesIdReportedIssue = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    EmiratesIdReportedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    EmiratesIdFrontBlobId = table.Column<int>(type: "int", nullable: true),
                    EmiratesIdBackBlobId = table.Column<int>(type: "int", nullable: true),
                    EmiratesIdVerifiedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    EmiratesIdVerifiedByName = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: true),
                    DfmStatus = table.Column<int>(type: "int", nullable: false),
                    DfmSource = table.Column<int>(type: "int", nullable: false),
                    NinResponse = table.Column<int>(type: "int", nullable: false),
                    NinNumber = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    NinRegisteredName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    DfmOtherInformation = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    DfmConfirmedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DfmReportedIssue = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    DfmReportedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    NinDocumentBlobId = table.Column<int>(type: "int", nullable: true),
                    DfmVerifiedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DfmVerifiedByName = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MemberOnboardings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MemberOnboardings_Members_MemberId",
                        column: x => x.MemberId,
                        principalTable: "Members",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MemberOnboardings_MemberId",
                table: "MemberOnboardings",
                column: "MemberId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MemberOnboardings");
        }
    }
}
