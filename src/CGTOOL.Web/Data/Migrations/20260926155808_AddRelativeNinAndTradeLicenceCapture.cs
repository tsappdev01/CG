using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CGTOOL.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddRelativeNinAndTradeLicenceCapture : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Drop and add rather than rename. The old column held RelatedPartyInterestType
            // (None/Direct/Indirect); the new one holds RelatedPartyHoldingNature
            // (None/Affiliate/Owned/Subsidiary). A rename keeps the stored integers, so every
            // "Direct" would silently read back as "Affiliate" and every "Indirect" as "Owned" --
            // answers nobody gave. Direct/Indirect have no equivalent in the new list, so the
            // rows reset to None and are re-stated by the member.
            migrationBuilder.DropColumn(
                name: "InterestType",
                table: "FamilyMembers");

            migrationBuilder.AddColumn<int>(
                name: "NatureOfHolding",
                table: "FamilyMembers",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "TradeLicenceExpiryDate",
                table: "OwnedCompanies",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TradeLicenceLegalName",
                table: "OwnedCompanies",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TradeLicenceNumber",
                table: "OwnedCompanies",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NinNumber",
                table: "FamilyMembers",
                type: "nvarchar(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "TradeLicenceExpiryDate",
                table: "FamilyMembers",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TradeLicenceLegalName",
                table: "FamilyMembers",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TradeLicenceNumber",
                table: "FamilyMembers",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TradeLicencePath",
                table: "FamilyMembers",
                type: "nvarchar(260)",
                maxLength: 260,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TradeLicenceExpiryDate",
                table: "OwnedCompanies");

            migrationBuilder.DropColumn(
                name: "TradeLicenceLegalName",
                table: "OwnedCompanies");

            migrationBuilder.DropColumn(
                name: "TradeLicenceNumber",
                table: "OwnedCompanies");

            migrationBuilder.DropColumn(
                name: "NinNumber",
                table: "FamilyMembers");

            migrationBuilder.DropColumn(
                name: "TradeLicenceExpiryDate",
                table: "FamilyMembers");

            migrationBuilder.DropColumn(
                name: "TradeLicenceLegalName",
                table: "FamilyMembers");

            migrationBuilder.DropColumn(
                name: "TradeLicenceNumber",
                table: "FamilyMembers");

            migrationBuilder.DropColumn(
                name: "TradeLicencePath",
                table: "FamilyMembers");

            migrationBuilder.DropColumn(
                name: "NatureOfHolding",
                table: "FamilyMembers");

            migrationBuilder.AddColumn<int>(
                name: "InterestType",
                table: "FamilyMembers",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }
    }
}
