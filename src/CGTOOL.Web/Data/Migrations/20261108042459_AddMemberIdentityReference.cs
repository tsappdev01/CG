using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CGTOOL.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMemberIdentityReference : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MemberIdentityReferences",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Email = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: false),
                    FullName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    CompanyName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Nin = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    NameOnEmiratesId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    EmiratesIdNumber = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Nationality = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    EmiratesIdBlobId = table.Column<int>(type: "int", nullable: true),
                    EmiratesIdFileName = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: true),
                    ImportedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    SourceFileName = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MemberIdentityReferences", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MemberIdentityReferences");
        }
    }
}
