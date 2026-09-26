using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CGTOOL.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddFamilyMemberHoldings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FamilyMemberHoldings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FamilyMemberId = table.Column<int>(type: "int", nullable: false),
                    CompanyName = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    NatureOfHolding = table.Column<int>(type: "int", nullable: false),
                    OwnershipPercentage = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FamilyMemberHoldings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FamilyMemberHoldings_FamilyMembers_FamilyMemberId",
                        column: x => x.FamilyMemberId,
                        principalTable: "FamilyMembers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FamilyMemberHoldings_FamilyMemberId",
                table: "FamilyMemberHoldings",
                column: "FamilyMemberId");

            // The data first, then the columns go. Scaffolded, the drops came before the table
            // existed, which would have thrown every relative's company away -- and a migration
            // that loses what it was written to reshape is worse than no migration.
            migrationBuilder.Sql("""
                INSERT INTO dbo.FamilyMemberHoldings (FamilyMemberId, CompanyName, NatureOfHolding, OwnershipPercentage)
                SELECT Id, Organization, NatureOfHolding, OwnershipPercentage
                FROM dbo.FamilyMembers
                WHERE Organization IS NOT NULL AND LTRIM(RTRIM(Organization)) <> '';
                """);

            migrationBuilder.DropColumn(
                name: "NatureOfHolding",
                table: "FamilyMembers");

            migrationBuilder.DropColumn(
                name: "Organization",
                table: "FamilyMembers");

            migrationBuilder.DropColumn(
                name: "OwnershipPercentage",
                table: "FamilyMembers");

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FamilyMemberHoldings");

            migrationBuilder.AddColumn<int>(
                name: "NatureOfHolding",
                table: "FamilyMembers",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Organization",
                table: "FamilyMembers",
                type: "nvarchar(160)",
                maxLength: 160,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "OwnershipPercentage",
                table: "FamilyMembers",
                type: "decimal(5,2)",
                precision: 5,
                scale: 2,
                nullable: true);
        }
    }
}
