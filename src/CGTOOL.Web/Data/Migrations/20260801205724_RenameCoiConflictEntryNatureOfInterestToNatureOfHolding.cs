using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CGTOOL.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class RenameCoiConflictEntryNatureOfInterestToNatureOfHolding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "NatureOfInterest",
                table: "CoiConflictEntries",
                newName: "NatureOfHolding");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "NatureOfHolding",
                table: "CoiConflictEntries",
                newName: "NatureOfInterest");
        }
    }
}
