using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CGTOOL.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddRelatedPartyTransaction : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RelatedPartyTransactions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CompanyId = table.Column<int>(type: "int", nullable: false),
                    MemberId = table.Column<int>(type: "int", nullable: false),
                    CounterPartyName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    TransactionValue = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    DateOfRequest = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ApproverMemberId = table.Column<int>(type: "int", nullable: true),
                    ApproverAction = table.Column<int>(type: "int", nullable: true),
                    ApproverRemarks = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    ApproverActionAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    EscalationReason = table.Column<int>(type: "int", nullable: false),
                    EscalatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CcaoAction = table.Column<int>(type: "int", nullable: true),
                    CcaoRemarks = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    CcaoActionAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DocumentationConfirmed = table.Column<bool>(type: "bit", nullable: false),
                    ReleasedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    AmendmentCount = table.Column<int>(type: "int", nullable: false),
                    LastReminderSentUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    SubmittedByName = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    SubmittedOnBehalfOf = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RelatedPartyTransactions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RelatedPartyTransactions_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RelatedPartyTransactions_Members_ApproverMemberId",
                        column: x => x.ApproverMemberId,
                        principalTable: "Members",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RelatedPartyTransactions_Members_MemberId",
                        column: x => x.MemberId,
                        principalTable: "Members",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RelatedPartyTransactionSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    CcaoMemberId = table.Column<int>(type: "int", nullable: true),
                    MdCeoMemberId = table.Column<int>(type: "int", nullable: true),
                    GroupCfoMemberId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RelatedPartyTransactionSettings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RelatedPartyTransactionSettings_Members_CcaoMemberId",
                        column: x => x.CcaoMemberId,
                        principalTable: "Members",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RelatedPartyTransactionSettings_Members_GroupCfoMemberId",
                        column: x => x.GroupCfoMemberId,
                        principalTable: "Members",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RelatedPartyTransactionSettings_Members_MdCeoMemberId",
                        column: x => x.MdCeoMemberId,
                        principalTable: "Members",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RelatedPartyTransactions_ApproverMemberId",
                table: "RelatedPartyTransactions",
                column: "ApproverMemberId");

            migrationBuilder.CreateIndex(
                name: "IX_RelatedPartyTransactions_CompanyId",
                table: "RelatedPartyTransactions",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_RelatedPartyTransactions_MemberId",
                table: "RelatedPartyTransactions",
                column: "MemberId");

            migrationBuilder.CreateIndex(
                name: "IX_RelatedPartyTransactions_Status",
                table: "RelatedPartyTransactions",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_RelatedPartyTransactionSettings_CcaoMemberId",
                table: "RelatedPartyTransactionSettings",
                column: "CcaoMemberId");

            migrationBuilder.CreateIndex(
                name: "IX_RelatedPartyTransactionSettings_GroupCfoMemberId",
                table: "RelatedPartyTransactionSettings",
                column: "GroupCfoMemberId");

            migrationBuilder.CreateIndex(
                name: "IX_RelatedPartyTransactionSettings_MdCeoMemberId",
                table: "RelatedPartyTransactionSettings",
                column: "MdCeoMemberId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RelatedPartyTransactions");

            migrationBuilder.DropTable(
                name: "RelatedPartyTransactionSettings");
        }
    }
}
