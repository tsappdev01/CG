using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CGTOOL.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMemberDeclarationType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DeclarationType",
                table: "Members",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DeclarationType",
                table: "Members");
        }
    }
}
