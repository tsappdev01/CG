using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CGTOOL.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMemberNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // defaultValueSql (not a fixed defaultValue) so existing Members get "now" at migration time,
            // not 0001-01-01 -- that would make every pre-existing user look stale/never-modified.
            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAtUtc",
                table: "Members",
                type: "datetime2",
                nullable: false,
                defaultValueSql: "SYSUTCDATETIME()");

            migrationBuilder.AddColumn<DateTime>(
                name: "ModifiedAtUtc",
                table: "Members",
                type: "datetime2",
                nullable: false,
                defaultValueSql: "SYSUTCDATETIME()");

            migrationBuilder.CreateTable(
                name: "MemberNotifications",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MemberId = table.Column<int>(type: "int", nullable: false),
                    Recipient = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    SentAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MemberNotifications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MemberNotifications_Members_MemberId",
                        column: x => x.MemberId,
                        principalTable: "Members",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MemberNotifications_MemberId",
                table: "MemberNotifications",
                column: "MemberId");

            migrationBuilder.CreateIndex(
                name: "IX_MemberNotifications_Status",
                table: "MemberNotifications",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MemberNotifications");

            migrationBuilder.DropColumn(
                name: "CreatedAtUtc",
                table: "Members");

            migrationBuilder.DropColumn(
                name: "ModifiedAtUtc",
                table: "Members");
        }
    }
}
