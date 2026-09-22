using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CGTOOL.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddRelatedPartyTransactionDocument : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RelatedPartyTransactionDocuments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RelatedPartyTransactionId = table.Column<int>(type: "int", nullable: false),
                    FilePath = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    UploadedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RelatedPartyTransactionDocuments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RelatedPartyTransactionDocuments_RelatedPartyTransactions_RelatedPartyTransactionId",
                        column: x => x.RelatedPartyTransactionId,
                        principalTable: "RelatedPartyTransactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RelatedPartyTransactionDocuments_RelatedPartyTransactionId",
                table: "RelatedPartyTransactionDocuments",
                column: "RelatedPartyTransactionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RelatedPartyTransactionDocuments");
        }
    }
}
