using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CGTOOL.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDeclarationCycleSetup : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DeclarationCycleSetups",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Type = table.Column<int>(type: "int", nullable: false),
                    FrequencyUnit = table.Column<int>(type: "int", nullable: false),
                    DueDateOffsetDays = table.Column<int>(type: "int", nullable: false),
                    ReminderCount = table.Column<int>(type: "int", nullable: false),
                    ReminderFrequency = table.Column<int>(type: "int", nullable: false),
                    EmailSubject = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    EmailBody = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Active = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeclarationCycleSetups", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DeclarationCycleRuns",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DeclarationCycleSetupId = table.Column<int>(type: "int", nullable: false),
                    Type = table.Column<int>(type: "int", nullable: false),
                    CompanyId = table.Column<int>(type: "int", nullable: true),
                    SentAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DueDateUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RecipientCount = table.Column<int>(type: "int", nullable: false),
                    RemindersSent = table.Column<int>(type: "int", nullable: false),
                    LastReminderSentUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeclarationCycleRuns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DeclarationCycleRuns_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DeclarationCycleRuns_DeclarationCycleSetups_DeclarationCycleSetupId",
                        column: x => x.DeclarationCycleSetupId,
                        principalTable: "DeclarationCycleSetups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DeclarationCycleRuns_CompanyId",
                table: "DeclarationCycleRuns",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_DeclarationCycleRuns_DeclarationCycleSetupId",
                table: "DeclarationCycleRuns",
                column: "DeclarationCycleSetupId");

            migrationBuilder.CreateIndex(
                name: "IX_DeclarationCycleSetups_Type",
                table: "DeclarationCycleSetups",
                column: "Type",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DeclarationCycleRuns");

            migrationBuilder.DropTable(
                name: "DeclarationCycleSetups");
        }
    }
}
