using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CGTOOL.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddIdentityReferenceArabicNameAndExpiry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "EmiratesIdExpiryDate",
                table: "MemberIdentityReferences",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NameOnEmiratesIdArabic",
                table: "MemberIdentityReferences",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EmiratesIdExpiryDate",
                table: "MemberIdentityReferences");

            migrationBuilder.DropColumn(
                name: "NameOnEmiratesIdArabic",
                table: "MemberIdentityReferences");
        }
    }
}
