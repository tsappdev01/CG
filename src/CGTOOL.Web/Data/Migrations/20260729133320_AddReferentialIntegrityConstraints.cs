using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CGTOOL.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddReferentialIntegrityConstraints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ScheduledActivityDates_ScheduledActivityId",
                table: "ScheduledActivityDates");

            migrationBuilder.DropIndex(
                name: "IX_Members_ApplicationUserId",
                table: "Members");

            migrationBuilder.CreateIndex(
                name: "IX_ScheduledActivityDates_ScheduledActivityId_Month_Day",
                table: "ScheduledActivityDates",
                columns: new[] { "ScheduledActivityId", "Month", "Day" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Members_ApplicationUserId",
                table: "Members",
                column: "ApplicationUserId",
                unique: true,
                filter: "[ApplicationUserId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ScheduledActivityDates_ScheduledActivityId_Month_Day",
                table: "ScheduledActivityDates");

            migrationBuilder.DropIndex(
                name: "IX_Members_ApplicationUserId",
                table: "Members");

            migrationBuilder.CreateIndex(
                name: "IX_ScheduledActivityDates_ScheduledActivityId",
                table: "ScheduledActivityDates",
                column: "ScheduledActivityId");

            migrationBuilder.CreateIndex(
                name: "IX_Members_ApplicationUserId",
                table: "Members",
                column: "ApplicationUserId");
        }
    }
}
