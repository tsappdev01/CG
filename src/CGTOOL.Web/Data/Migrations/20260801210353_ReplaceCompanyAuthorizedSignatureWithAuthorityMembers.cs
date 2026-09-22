using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CGTOOL.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class ReplaceCompanyAuthorizedSignatureWithAuthorityMembers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AuthorizedSignatureDepartment",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "AuthorizedSignatureDesignation",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "AuthorizedSignatureEmail",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "AuthorizedSignatureName",
                table: "Companies");

            migrationBuilder.AddColumn<int>(
                name: "ApprovingAuthorityMemberId",
                table: "Companies",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DelegateAuthorityMemberId",
                table: "Companies",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Companies_ApprovingAuthorityMemberId",
                table: "Companies",
                column: "ApprovingAuthorityMemberId");

            migrationBuilder.CreateIndex(
                name: "IX_Companies_DelegateAuthorityMemberId",
                table: "Companies",
                column: "DelegateAuthorityMemberId");

            migrationBuilder.AddForeignKey(
                name: "FK_Companies_Members_ApprovingAuthorityMemberId",
                table: "Companies",
                column: "ApprovingAuthorityMemberId",
                principalTable: "Members",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Companies_Members_DelegateAuthorityMemberId",
                table: "Companies",
                column: "DelegateAuthorityMemberId",
                principalTable: "Members",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Companies_Members_ApprovingAuthorityMemberId",
                table: "Companies");

            migrationBuilder.DropForeignKey(
                name: "FK_Companies_Members_DelegateAuthorityMemberId",
                table: "Companies");

            migrationBuilder.DropIndex(
                name: "IX_Companies_ApprovingAuthorityMemberId",
                table: "Companies");

            migrationBuilder.DropIndex(
                name: "IX_Companies_DelegateAuthorityMemberId",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "ApprovingAuthorityMemberId",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "DelegateAuthorityMemberId",
                table: "Companies");

            migrationBuilder.AddColumn<string>(
                name: "AuthorizedSignatureDepartment",
                table: "Companies",
                type: "nvarchar(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AuthorizedSignatureDesignation",
                table: "Companies",
                type: "nvarchar(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AuthorizedSignatureEmail",
                table: "Companies",
                type: "nvarchar(160)",
                maxLength: 160,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AuthorizedSignatureName",
                table: "Companies",
                type: "nvarchar(120)",
                maxLength: 120,
                nullable: true);
        }
    }
}
