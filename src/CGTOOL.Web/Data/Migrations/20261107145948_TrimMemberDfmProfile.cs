using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CGTOOL.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class TrimMemberDfmProfile : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AccountStatus",
                table: "MemberDfmProfiles");

            migrationBuilder.DropColumn(
                name: "City",
                table: "MemberDfmProfiles");

            migrationBuilder.DropColumn(
                name: "Country",
                table: "MemberDfmProfiles");

            migrationBuilder.DropColumn(
                name: "DateOfBirth",
                table: "MemberDfmProfiles");

            migrationBuilder.DropColumn(
                name: "Gender",
                table: "MemberDfmProfiles");

            migrationBuilder.DropColumn(
                name: "LastUpdatedDate",
                table: "MemberDfmProfiles");

            migrationBuilder.DropColumn(
                name: "PoBox",
                table: "MemberDfmProfiles");

            migrationBuilder.DropColumn(
                name: "RegistrationDate",
                table: "MemberDfmProfiles");

            migrationBuilder.DropColumn(
                name: "ResidentialAddress",
                table: "MemberDfmProfiles");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AccountStatus",
                table: "MemberDfmProfiles",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "City",
                table: "MemberDfmProfiles",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Country",
                table: "MemberDfmProfiles",
                type: "nvarchar(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DateOfBirth",
                table: "MemberDfmProfiles",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Gender",
                table: "MemberDfmProfiles",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastUpdatedDate",
                table: "MemberDfmProfiles",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PoBox",
                table: "MemberDfmProfiles",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RegistrationDate",
                table: "MemberDfmProfiles",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ResidentialAddress",
                table: "MemberDfmProfiles",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: true);
        }
    }
}
