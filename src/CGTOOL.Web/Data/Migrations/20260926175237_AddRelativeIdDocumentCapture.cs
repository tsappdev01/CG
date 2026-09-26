using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CGTOOL.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddRelativeIdDocumentCapture : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "EmiratesIdExpiryDate",
                table: "FamilyMembers",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EmiratesIdNumber",
                table: "FamilyMembers",
                type: "nvarchar(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PassportExpiryDate",
                table: "FamilyMembers",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PassportNumber",
                table: "FamilyMembers",
                type: "nvarchar(60)",
                maxLength: 60,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EmiratesIdExpiryDate",
                table: "FamilyMembers");

            migrationBuilder.DropColumn(
                name: "EmiratesIdNumber",
                table: "FamilyMembers");

            migrationBuilder.DropColumn(
                name: "PassportExpiryDate",
                table: "FamilyMembers");

            migrationBuilder.DropColumn(
                name: "PassportNumber",
                table: "FamilyMembers");
        }
    }
}
