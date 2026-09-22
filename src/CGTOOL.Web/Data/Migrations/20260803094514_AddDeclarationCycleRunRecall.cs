using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CGTOOL.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDeclarationCycleRunRecall : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "Recalled",
                table: "DeclarationCycleRuns",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "RecalledAtUtc",
                table: "DeclarationCycleRuns",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RecalledByName",
                table: "DeclarationCycleRuns",
                type: "nvarchar(160)",
                maxLength: 160,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Recalled",
                table: "DeclarationCycleRuns");

            migrationBuilder.DropColumn(
                name: "RecalledAtUtc",
                table: "DeclarationCycleRuns");

            migrationBuilder.DropColumn(
                name: "RecalledByName",
                table: "DeclarationCycleRuns");
        }
    }
}
