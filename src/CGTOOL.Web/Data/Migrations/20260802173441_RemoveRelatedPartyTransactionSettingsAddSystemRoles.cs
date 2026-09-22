using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CGTOOL.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class RemoveRelatedPartyTransactionSettingsAddSystemRoles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RelatedPartyTransactionSettings");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RelatedPartyTransactionSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    CcaoMemberId = table.Column<int>(type: "int", nullable: true),
                    GroupCfoMemberId = table.Column<int>(type: "int", nullable: true),
                    MdCeoMemberId = table.Column<int>(type: "int", nullable: true)
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
    }
}
