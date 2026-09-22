using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CGTOOL.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddRelatedPartyCoiDeclaration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RelatedPartyCoiDeclarations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MemberId = table.Column<int>(type: "int", nullable: false),
                    DeclarationCycleRunId = table.Column<int>(type: "int", nullable: false),
                    NothingToDeclareRelatives = table.Column<bool>(type: "bit", nullable: false),
                    NothingToDeclareSelfOwned = table.Column<bool>(type: "bit", nullable: false),
                    NothingToDeclareRelativeOwned = table.Column<bool>(type: "bit", nullable: false),
                    NothingToDeclareBoardRoles = table.Column<bool>(type: "bit", nullable: false),
                    NothingToDeclareConflicts = table.Column<bool>(type: "bit", nullable: false),
                    IsDraft = table.Column<bool>(type: "bit", nullable: false),
                    AttestationName = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    AttestationConfirmed = table.Column<bool>(type: "bit", nullable: false),
                    SubmittedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    SubmittedByName = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    SubmittedOnBehalfOf = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RelatedPartyCoiDeclarations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RelatedPartyCoiDeclarations_DeclarationCycleRuns_DeclarationCycleRunId",
                        column: x => x.DeclarationCycleRunId,
                        principalTable: "DeclarationCycleRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RelatedPartyCoiDeclarations_Members_MemberId",
                        column: x => x.MemberId,
                        principalTable: "Members",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CoiConflictEntries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RelatedPartyCoiDeclarationId = table.Column<int>(type: "int", nullable: false),
                    CompanyOrCounterpartyName = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    PrincipalBusinessActivity = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    NatureOfInterest = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CoiConflictEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CoiConflictEntries_RelatedPartyCoiDeclarations_RelatedPartyCoiDeclarationId",
                        column: x => x.RelatedPartyCoiDeclarationId,
                        principalTable: "RelatedPartyCoiDeclarations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CoiRelatives",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RelatedPartyCoiDeclarationId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Relationship = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CoiRelatives", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CoiRelatives_RelatedPartyCoiDeclarations_RelatedPartyCoiDeclarationId",
                        column: x => x.RelatedPartyCoiDeclarationId,
                        principalTable: "RelatedPartyCoiDeclarations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CoiCompanyEntries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RelatedPartyCoiDeclarationId = table.Column<int>(type: "int", nullable: false),
                    OwnerType = table.Column<int>(type: "int", nullable: false),
                    CoiRelativeId = table.Column<int>(type: "int", nullable: true),
                    LegalCompanyName = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    PrincipalBusinessActivity = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    TradeLicenseNumber = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    TradeLicenseExpiryDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LicenseActivities = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CoiCompanyEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CoiCompanyEntries_CoiRelatives_CoiRelativeId",
                        column: x => x.CoiRelativeId,
                        principalTable: "CoiRelatives",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_CoiCompanyEntries_RelatedPartyCoiDeclarations_RelatedPartyCoiDeclarationId",
                        column: x => x.RelatedPartyCoiDeclarationId,
                        principalTable: "RelatedPartyCoiDeclarations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CoiTradeLicenseDocuments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CoiCompanyEntryId = table.Column<int>(type: "int", nullable: false),
                    FilePath = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    UploadedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CoiTradeLicenseDocuments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CoiTradeLicenseDocuments_CoiCompanyEntries_CoiCompanyEntryId",
                        column: x => x.CoiCompanyEntryId,
                        principalTable: "CoiCompanyEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CoiCompanyEntries_CoiRelativeId",
                table: "CoiCompanyEntries",
                column: "CoiRelativeId");

            migrationBuilder.CreateIndex(
                name: "IX_CoiCompanyEntries_RelatedPartyCoiDeclarationId",
                table: "CoiCompanyEntries",
                column: "RelatedPartyCoiDeclarationId");

            migrationBuilder.CreateIndex(
                name: "IX_CoiConflictEntries_RelatedPartyCoiDeclarationId",
                table: "CoiConflictEntries",
                column: "RelatedPartyCoiDeclarationId");

            migrationBuilder.CreateIndex(
                name: "IX_CoiRelatives_RelatedPartyCoiDeclarationId",
                table: "CoiRelatives",
                column: "RelatedPartyCoiDeclarationId");

            migrationBuilder.CreateIndex(
                name: "IX_CoiTradeLicenseDocuments_CoiCompanyEntryId",
                table: "CoiTradeLicenseDocuments",
                column: "CoiCompanyEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_RelatedPartyCoiDeclarations_DeclarationCycleRunId",
                table: "RelatedPartyCoiDeclarations",
                column: "DeclarationCycleRunId");

            migrationBuilder.CreateIndex(
                name: "IX_RelatedPartyCoiDeclarations_MemberId",
                table: "RelatedPartyCoiDeclarations",
                column: "MemberId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CoiConflictEntries");

            migrationBuilder.DropTable(
                name: "CoiTradeLicenseDocuments");

            migrationBuilder.DropTable(
                name: "CoiCompanyEntries");

            migrationBuilder.DropTable(
                name: "CoiRelatives");

            migrationBuilder.DropTable(
                name: "RelatedPartyCoiDeclarations");
        }
    }
}
