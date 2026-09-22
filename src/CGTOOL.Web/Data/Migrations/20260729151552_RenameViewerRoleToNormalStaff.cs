using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CGTOOL.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class RenameViewerRoleToNormalStaff : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The built-in "Viewer" role is renamed to "Normal Staff" (a low-privilege role
            // restricted to submitting the user's own Insider Declaration and Related Party/COI
            // declaration). Any user already assigned "Viewer" keeps their assignment — only the
            // role's name changes, via AspNetRoleId, so AspNetUserRoles is unaffected.
            migrationBuilder.Sql(
                "UPDATE [AspNetRoles] SET [Name] = N'Normal Staff', [NormalizedName] = N'NORMAL STAFF' WHERE [NormalizedName] = N'VIEWER';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "UPDATE [AspNetRoles] SET [Name] = N'Viewer', [NormalizedName] = N'VIEWER' WHERE [NormalizedName] = N'NORMAL STAFF';");
        }
    }
}
