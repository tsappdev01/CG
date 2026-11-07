using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CGTOOL.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class ShareEntryHolderMatchesGrid : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "HolderKind",
                table: "InsiderDeclarationShareEntries",
                newName: "Relationship");

            migrationBuilder.AddColumn<bool>(
                name: "IsSelf",
                table: "InsiderDeclarationShareEntries",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsSelf",
                table: "InsiderDeclarationShareEntries");

            migrationBuilder.RenameColumn(
                name: "Relationship",
                table: "InsiderDeclarationShareEntries",
                newName: "HolderKind");
        }
    }
}
