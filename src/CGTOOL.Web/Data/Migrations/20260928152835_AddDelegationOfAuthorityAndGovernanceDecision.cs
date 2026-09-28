using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CGTOOL.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDelegationOfAuthorityAndGovernanceDecision : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AbstainedMembers",
                table: "RelatedPartyTransactions",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "GoverningBody",
                table: "RelatedPartyTransactions",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "GoverningBodyDecisionDate",
                table: "RelatedPartyTransactions",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Kind",
                table: "RelatedPartyTransactionDocuments",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "DelegationsOfAuthority",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CompanyId = table.Column<int>(type: "int", nullable: false),
                    EffectiveFrom = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ApproverLimit = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    EscalateOnApproverConflict = table.Column<bool>(type: "bit", nullable: false),
                    EscalateAfterDays = table.Column<int>(type: "int", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DelegationsOfAuthority", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DelegationsOfAuthority_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DelegationOfAuthorityBands",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DelegationOfAuthorityId = table.Column<int>(type: "int", nullable: false),
                    FromValue = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    ToValue = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    Authority = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DelegationOfAuthorityBands", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DelegationOfAuthorityBands_DelegationsOfAuthority_DelegationOfAuthorityId",
                        column: x => x.DelegationOfAuthorityId,
                        principalTable: "DelegationsOfAuthority",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DelegationOfAuthorityBands_DelegationOfAuthorityId",
                table: "DelegationOfAuthorityBands",
                column: "DelegationOfAuthorityId");

            migrationBuilder.CreateIndex(
                name: "IX_DelegationsOfAuthority_CompanyId",
                table: "DelegationsOfAuthority",
                column: "CompanyId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DelegationOfAuthorityBands");

            migrationBuilder.DropTable(
                name: "DelegationsOfAuthority");

            migrationBuilder.DropColumn(
                name: "AbstainedMembers",
                table: "RelatedPartyTransactions");

            migrationBuilder.DropColumn(
                name: "GoverningBody",
                table: "RelatedPartyTransactions");

            migrationBuilder.DropColumn(
                name: "GoverningBodyDecisionDate",
                table: "RelatedPartyTransactions");

            migrationBuilder.DropColumn(
                name: "Kind",
                table: "RelatedPartyTransactionDocuments");
        }
    }
}
