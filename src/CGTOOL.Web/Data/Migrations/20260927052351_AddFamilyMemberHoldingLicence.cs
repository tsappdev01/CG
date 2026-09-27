using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CGTOOL.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddFamilyMemberHoldingLicence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "LicenceActivities",
                table: "FamilyMemberHoldings",
                type: "nvarchar(400)",
                maxLength: 400,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PrincipalBusinessActivity",
                table: "FamilyMemberHoldings",
                type: "nvarchar(400)",
                maxLength: 400,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "TradeLicenceExpiryDate",
                table: "FamilyMemberHoldings",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TradeLicenceFileName",
                table: "FamilyMemberHoldings",
                type: "nvarchar(260)",
                maxLength: 260,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TradeLicenceNumber",
                table: "FamilyMemberHoldings",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TradeLicencePath",
                table: "FamilyMemberHoldings",
                type: "nvarchar(260)",
                maxLength: 260,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LicenceActivities",
                table: "FamilyMemberHoldings");

            migrationBuilder.DropColumn(
                name: "PrincipalBusinessActivity",
                table: "FamilyMemberHoldings");

            migrationBuilder.DropColumn(
                name: "TradeLicenceExpiryDate",
                table: "FamilyMemberHoldings");

            migrationBuilder.DropColumn(
                name: "TradeLicenceFileName",
                table: "FamilyMemberHoldings");

            migrationBuilder.DropColumn(
                name: "TradeLicenceNumber",
                table: "FamilyMemberHoldings");

            migrationBuilder.DropColumn(
                name: "TradeLicencePath",
                table: "FamilyMemberHoldings");
        }
    }
}
