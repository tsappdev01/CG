using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CGTOOL.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddInsiderDeclarationDraftAndIdUploads : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "EmiratesIdPath",
                table: "InsiderDeclarations",
                type: "nvarchar(260)",
                maxLength: 260,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDraft",
                table: "InsiderDeclarations",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "PassportPath",
                table: "InsiderDeclarations",
                type: "nvarchar(260)",
                maxLength: 260,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EmiratesIdPath",
                table: "InsiderDeclarations");

            migrationBuilder.DropColumn(
                name: "IsDraft",
                table: "InsiderDeclarations");

            migrationBuilder.DropColumn(
                name: "PassportPath",
                table: "InsiderDeclarations");
        }
    }
}
