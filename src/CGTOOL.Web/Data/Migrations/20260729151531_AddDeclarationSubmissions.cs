using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CGTOOL.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDeclarationSubmissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DeclarationSubmissions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MemberId = table.Column<int>(type: "int", nullable: false),
                    DeclarationSetupId = table.Column<int>(type: "int", nullable: false),
                    SubmittedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeclarationSubmissions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DeclarationSubmissions_DeclarationSetups_DeclarationSetupId",
                        column: x => x.DeclarationSetupId,
                        principalTable: "DeclarationSetups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DeclarationSubmissions_Members_MemberId",
                        column: x => x.MemberId,
                        principalTable: "Members",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DeclarationSubmissions_DeclarationSetupId",
                table: "DeclarationSubmissions",
                column: "DeclarationSetupId");

            migrationBuilder.CreateIndex(
                name: "IX_DeclarationSubmissions_MemberId_DeclarationSetupId",
                table: "DeclarationSubmissions",
                columns: new[] { "MemberId", "DeclarationSetupId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DeclarationSubmissions");
        }
    }
}
