using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CGTOOL.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddInsiderShareEntries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "InsiderDeclarationShareEntries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    InsiderDeclarationId = table.Column<int>(type: "int", nullable: false),
                    HolderKind = table.Column<int>(type: "int", nullable: false),
                    HolderName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    HolderNin = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    EntryKind = table.Column<int>(type: "int", nullable: false),
                    TransactionDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    NumberOfShares = table.Column<long>(type: "bigint", nullable: false),
                    Rate = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InsiderDeclarationShareEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InsiderDeclarationShareEntries_InsiderDeclarations_InsiderDeclarationId",
                        column: x => x.InsiderDeclarationId,
                        principalTable: "InsiderDeclarations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InsiderDeclarationShareEntries_InsiderDeclarationId",
                table: "InsiderDeclarationShareEntries",
                column: "InsiderDeclarationId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InsiderDeclarationShareEntries");
        }
    }
}
