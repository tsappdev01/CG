using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CGTOOL.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDeclarationCycleRunPeriod : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PeriodQuarter",
                table: "DeclarationCycleRuns",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "PeriodYear",
                table: "DeclarationCycleRuns",
                type: "int",
                nullable: false,
                defaultValue: 0);

            // Backfill existing rows (created before PeriodYear/PeriodQuarter existed) from the
            // calendar quarter containing SentAtUtc, matching how the app derived the period before
            // this change -- leaving them at the AddColumn default of 0/0 would break the compliance
            // report and the wizard's {From}/{To} merge fields for every declaration sent before today.
            migrationBuilder.Sql("""
                UPDATE dbo.DeclarationCycleRuns
                SET PeriodYear = DATEPART(YEAR, SentAtUtc),
                    PeriodQuarter = ((DATEPART(MONTH, SentAtUtc) - 1) / 3) + 1
                WHERE PeriodYear = 0;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PeriodQuarter",
                table: "DeclarationCycleRuns");

            migrationBuilder.DropColumn(
                name: "PeriodYear",
                table: "DeclarationCycleRuns");
        }
    }
}
