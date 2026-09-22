using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CGTOOL.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class RevampDeclarationCycleScheduling : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Active",
                table: "DeclarationCycleSetups");

            migrationBuilder.DropColumn(
                name: "DueDateOffsetDays",
                table: "DeclarationCycleSetups");

            migrationBuilder.DropColumn(
                name: "FrequencyUnit",
                table: "DeclarationCycleSetups");

            // Every pre-existing run was already sent under the old (no-scheduling) model, so backfill
            // Sent = 1 for them -- only newly created "Schedule Send" rows should start as pending.
            migrationBuilder.AddColumn<bool>(
                name: "Sent",
                table: "DeclarationCycleRuns",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.CreateTable(
                name: "DeclarationCycleRunRecipients",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DeclarationCycleRunId = table.Column<int>(type: "int", nullable: false),
                    MemberId = table.Column<int>(type: "int", nullable: false),
                    MemberName = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Email = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    CompanyName = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    SentAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Success = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeclarationCycleRunRecipients", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DeclarationCycleRunRecipients_DeclarationCycleRuns_DeclarationCycleRunId",
                        column: x => x.DeclarationCycleRunId,
                        principalTable: "DeclarationCycleRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DeclarationCycleRunRecipients_DeclarationCycleRunId",
                table: "DeclarationCycleRunRecipients",
                column: "DeclarationCycleRunId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DeclarationCycleRunRecipients");

            migrationBuilder.DropColumn(
                name: "Sent",
                table: "DeclarationCycleRuns");

            migrationBuilder.AddColumn<bool>(
                name: "Active",
                table: "DeclarationCycleSetups",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "DueDateOffsetDays",
                table: "DeclarationCycleSetups",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "FrequencyUnit",
                table: "DeclarationCycleSetups",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }
    }
}
