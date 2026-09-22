using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CGTOOL.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddInsiderDeclarations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "InsiderDeclarations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MemberId = table.Column<int>(type: "int", nullable: false),
                    DeclarationCycleRunId = table.Column<int>(type: "int", nullable: false),
                    HasNin = table.Column<bool>(type: "bit", nullable: false),
                    NinNumber = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    HoldsShares = table.Column<bool>(type: "bit", nullable: false),
                    SharesNinNumber = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    NumberOfSharesHeld = table.Column<int>(type: "int", nullable: true),
                    RelativesHoldShares = table.Column<bool>(type: "bit", nullable: false),
                    SubmittedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    SubmittedByName = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    SubmittedOnBehalfOf = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InsiderDeclarations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InsiderDeclarations_DeclarationCycleRuns_DeclarationCycleRunId",
                        column: x => x.DeclarationCycleRunId,
                        principalTable: "DeclarationCycleRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InsiderDeclarations_Members_MemberId",
                        column: x => x.MemberId,
                        principalTable: "Members",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "InsiderDeclarationRelatives",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    InsiderDeclarationId = table.Column<int>(type: "int", nullable: false),
                    NinNumber = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    RelativeName = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Relationship = table.Column<int>(type: "int", nullable: false),
                    NumberOfShares = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InsiderDeclarationRelatives", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InsiderDeclarationRelatives_InsiderDeclarations_InsiderDeclarationId",
                        column: x => x.InsiderDeclarationId,
                        principalTable: "InsiderDeclarations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InsiderDeclarationRelatives_InsiderDeclarationId",
                table: "InsiderDeclarationRelatives",
                column: "InsiderDeclarationId");

            migrationBuilder.CreateIndex(
                name: "IX_InsiderDeclarations_DeclarationCycleRunId",
                table: "InsiderDeclarations",
                column: "DeclarationCycleRunId");

            migrationBuilder.CreateIndex(
                name: "IX_InsiderDeclarations_MemberId_DeclarationCycleRunId",
                table: "InsiderDeclarations",
                columns: new[] { "MemberId", "DeclarationCycleRunId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InsiderDeclarationRelatives");

            migrationBuilder.DropTable(
                name: "InsiderDeclarations");
        }
    }
}
