using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CGTOOL.Web.Data.Migrations
{
    /// <summary>Reminders are always weekly now, so the Daily/Weekly choice is gone and what is
    /// configured instead is the day of the week and the time of day they go out -- read in UAE
    /// Standard Time.
    ///
    /// ReminderFrequency is dropped rather than renamed: its values were 0 = Daily and 1 = Weekly,
    /// which as a DayOfWeek would silently read as Sunday and Monday. Existing rows take the default,
    /// Saturday at 08:00, which is what they were all doing in practice.</summary>
    public partial class AddDeclarationReminderDayAndTime : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ReminderFrequency",
                table: "DeclarationCycleSetups");

            migrationBuilder.AddColumn<int>(
                name: "ReminderDayOfWeek",
                table: "DeclarationCycleSetups",
                type: "int",
                nullable: false,
                defaultValue: (int)DayOfWeek.Saturday);

            migrationBuilder.AddColumn<TimeOnly>(
                name: "ReminderTimeOfDay",
                table: "DeclarationCycleSetups",
                type: "time",
                nullable: false,
                defaultValue: new TimeOnly(8, 0, 0));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ReminderTimeOfDay",
                table: "DeclarationCycleSetups");

            migrationBuilder.DropColumn(
                name: "ReminderDayOfWeek",
                table: "DeclarationCycleSetups");

            migrationBuilder.AddColumn<int>(
                name: "ReminderFrequency",
                table: "DeclarationCycleSetups",
                type: "int",
                nullable: false,
                defaultValue: 1);
        }
    }
}
