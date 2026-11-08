using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CGTOOL.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class OnboardingSingleEmiratesIdDocument : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Onboarding asks for the card as one PDF now, so there is no second file to hold.
            //
            // EF warns about data loss here. The column is dropped rather than left because it has
            // nothing in it: it was only ever written by the two-upload screen, which has not been
            // deployed long enough for anyone to have finished one -- the records in UAT are all
            // at PendingUpload with no document at all. Worth a glance before running this
            // anywhere that has been live longer:
            //   SELECT COUNT(*) FROM dbo.MemberOnboardings WHERE EmiratesIdBackBlobId IS NOT NULL;
            migrationBuilder.DropColumn(
                name: "EmiratesIdBackBlobId",
                table: "MemberOnboardings");

            migrationBuilder.RenameColumn(
                name: "EmiratesIdFrontBlobId",
                table: "MemberOnboardings",
                newName: "EmiratesIdBlobId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "EmiratesIdBlobId",
                table: "MemberOnboardings",
                newName: "EmiratesIdFrontBlobId");

            migrationBuilder.AddColumn<int>(
                name: "EmiratesIdBackBlobId",
                table: "MemberOnboardings",
                type: "int",
                nullable: true);
        }
    }
}
