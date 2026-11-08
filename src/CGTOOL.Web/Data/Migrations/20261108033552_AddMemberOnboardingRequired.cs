using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CGTOOL.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMemberOnboardingRequired : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // true, not the false EF generates for a bool: the property's "= true" is a CLR
            // initialiser and says nothing about the column, so the generated default would mark
            // every member who already exists as not needing onboarding -- the opposite of the
            // intended default, silently, for the whole population. This value does double duty:
            // it backfills the existing rows and becomes the column's default for new ones.
            migrationBuilder.AddColumn<bool>(
                name: "OnboardingRequired",
                table: "Members",
                type: "bit",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "OnboardingRequired",
                table: "Members");
        }
    }
}
