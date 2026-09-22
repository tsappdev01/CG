using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CGTOOL.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddNavMenuItemLabel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "NavMenuItemLabels",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ItemKey = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    CustomLabel = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NavMenuItemLabels", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_NavMenuItemLabels_ItemKey",
                table: "NavMenuItemLabels",
                column: "ItemKey",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "NavMenuItemLabels");
        }
    }
}
