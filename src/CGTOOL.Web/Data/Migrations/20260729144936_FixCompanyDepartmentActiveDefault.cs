using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CGTOOL.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class FixCompanyDepartmentActiveDefault : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The migration that introduced the Active (soft-delete) column defaulted existing
            // rows to Active = 0 instead of 1. Since no deactivate feature existed before that
            // column existed, every Company/Department that predates it was, in fact, active —
            // this data fix reactivates them so they show up again in entity/department pickers.
            migrationBuilder.Sql("UPDATE [Companies] SET [Active] = 1 WHERE [Active] = 0;");
            migrationBuilder.Sql("UPDATE [Departments] SET [Active] = 1 WHERE [Active] = 0;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Intentionally a no-op: this is a one-way data correction, not a schema change.
        }
    }
}
