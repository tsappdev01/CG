using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CGTOOL.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddFamilyMemberHoldsDiShares : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "HoldsDiShares",
                table: "FamilyMembers",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "HoldsDiShares",
                table: "FamilyMembers");
        }
    }
}
