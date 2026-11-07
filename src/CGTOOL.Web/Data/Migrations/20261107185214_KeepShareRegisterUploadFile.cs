using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CGTOOL.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class KeepShareRegisterUploadFile : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FilePath",
                table: "ShareholderRegisterUploads",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FilePath",
                table: "ShareholderRegisterUploads");
        }
    }
}
