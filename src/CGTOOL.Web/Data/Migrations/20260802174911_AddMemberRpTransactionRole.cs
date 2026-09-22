using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CGTOOL.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMemberRpTransactionRole : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "RpTransactionRole",
                table: "Members",
                type: "int",
                nullable: false,
                defaultValue: 0);

            // CCAO/CFO/COO/MD&CEO were briefly seeded as ASP.NET Identity system roles in an earlier
            // revision of this feature; they're now a dedicated Member.RpTransactionRole field instead
            // (see the Member screen's "RP Transaction Role" dropdown), so any already-seeded rows and
            // their assignments are removed here to keep "System roles for this login" showing only
            // Administrator/Normal User again.
            migrationBuilder.Sql(@"
                DELETE FROM [AspNetUserRoles] WHERE [RoleId] IN (
                    SELECT [Id] FROM [AspNetRoles] WHERE [Name] IN (N'CCAO', N'CFO', N'COO', N'MD&CEO'));
                DELETE FROM [AspNetRoles] WHERE [Name] IN (N'CCAO', N'CFO', N'COO', N'MD&CEO');
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RpTransactionRole",
                table: "Members");
        }
    }
}
