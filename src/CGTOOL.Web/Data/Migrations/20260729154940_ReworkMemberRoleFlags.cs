using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CGTOOL.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class ReworkMemberRoleFlags : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // "Transaction Processing" and "Transaction Processing and Control" are merged into a
            // single "Related Party Transaction" flag, and a new standalone "Insider Trading" flag
            // is introduced. Both new columns are backfilled from whichever old flag(s) were set,
            // so anyone who previously qualified for Insider Declaration reminders via either old
            // flag keeps qualifying under the new one.
            migrationBuilder.AddColumn<bool>(
                name: "InsiderTradingAccess",
                table: "Members",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "RelatedPartyTransactionAccess",
                table: "Members",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.Sql(
                "UPDATE [Members] SET [InsiderTradingAccess] = 1, [RelatedPartyTransactionAccess] = 1 " +
                "WHERE [TransactionProcessingAccess] = 1 OR [TransactionProcessingAndControl] = 1;");

            migrationBuilder.DropColumn(
                name: "TransactionProcessingAccess",
                table: "Members");

            migrationBuilder.DropColumn(
                name: "TransactionProcessingAndControl",
                table: "Members");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "TransactionProcessingAccess",
                table: "Members",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "TransactionProcessingAndControl",
                table: "Members",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.Sql(
                "UPDATE [Members] SET [TransactionProcessingAccess] = [InsiderTradingAccess], " +
                "[TransactionProcessingAndControl] = [RelatedPartyTransactionAccess];");

            migrationBuilder.DropColumn(
                name: "InsiderTradingAccess",
                table: "Members");

            migrationBuilder.DropColumn(
                name: "RelatedPartyTransactionAccess",
                table: "Members");
        }
    }
}
