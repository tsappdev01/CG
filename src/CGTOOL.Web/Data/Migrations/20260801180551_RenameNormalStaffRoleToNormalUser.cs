using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CGTOOL.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class RenameNormalStaffRoleToNormalUser : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "UPDATE [AspNetRoles] SET [Name] = N'Normal User', [NormalizedName] = N'NORMAL USER' WHERE [NormalizedName] = N'NORMAL STAFF';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "UPDATE [AspNetRoles] SET [Name] = N'Normal Staff', [NormalizedName] = N'NORMAL STAFF' WHERE [NormalizedName] = N'NORMAL USER';");
        }
    }
}
