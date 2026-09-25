using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CGTOOL.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddRelatedPartyDetailsToFamilyMember : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "DateOfBirth",
                table: "FamilyMembers",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IdentificationNumber",
                table: "FamilyMembers",
                type: "nvarchar(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "InterestType",
                table: "FamilyMembers",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Nationality",
                table: "FamilyMembers",
                type: "nvarchar(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Occupation",
                table: "FamilyMembers",
                type: "nvarchar(160)",
                maxLength: 160,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Organization",
                table: "FamilyMembers",
                type: "nvarchar(160)",
                maxLength: 160,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "OwnershipPercentage",
                table: "FamilyMembers",
                type: "decimal(5,2)",
                precision: 5,
                scale: 2,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DateOfBirth",
                table: "FamilyMembers");

            migrationBuilder.DropColumn(
                name: "IdentificationNumber",
                table: "FamilyMembers");

            migrationBuilder.DropColumn(
                name: "InterestType",
                table: "FamilyMembers");

            migrationBuilder.DropColumn(
                name: "Nationality",
                table: "FamilyMembers");

            migrationBuilder.DropColumn(
                name: "Occupation",
                table: "FamilyMembers");

            migrationBuilder.DropColumn(
                name: "Organization",
                table: "FamilyMembers");

            migrationBuilder.DropColumn(
                name: "OwnershipPercentage",
                table: "FamilyMembers");
        }
    }
}
