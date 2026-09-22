using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CGTOOL.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCompanyDepartmentMemberAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Transactions_Employees_EmployeeId",
                table: "Transactions");

            migrationBuilder.DropTable(
                name: "Employees");

            migrationBuilder.RenameColumn(
                name: "EmployeeId",
                table: "Transactions",
                newName: "MemberId");

            migrationBuilder.RenameIndex(
                name: "IX_Transactions_EmployeeId",
                table: "Transactions",
                newName: "IX_Transactions_MemberId");

            migrationBuilder.CreateTable(
                name: "AuditLogEntries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ActorDisplayName = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    ActingOnBehalfOf = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: true),
                    Action = table.Column<int>(type: "int", nullable: false),
                    EntityType = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    EntityId = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    Details = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditLogEntries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Companies",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    ShortCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Address = table.Column<string>(type: "nvarchar(240)", maxLength: 240, nullable: true),
                    City = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    Country = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    AuthorizedSignatureName = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    AuthorizedSignatureDesignation = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    AuthorizedSignatureDepartment = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Companies", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Departments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Departments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Members",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CompanyId = table.Column<int>(type: "int", nullable: false),
                    FullName = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    JobTitle = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    DepartmentId = table.Column<int>(type: "int", nullable: true),
                    Email = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: true),
                    Signature = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: true),
                    ReportingManagerId = table.Column<int>(type: "int", nullable: true),
                    IsBoardMember = table.Column<bool>(type: "bit", nullable: false),
                    IsManualEntry = table.Column<bool>(type: "bit", nullable: false),
                    IsExternalMember = table.Column<bool>(type: "bit", nullable: false),
                    Active = table.Column<bool>(type: "bit", nullable: false),
                    RelatedPartyRegisterAccess = table.Column<bool>(type: "bit", nullable: false),
                    ConflictOfInterestAccess = table.Column<bool>(type: "bit", nullable: false),
                    TransactionProcessingAccess = table.Column<bool>(type: "bit", nullable: false),
                    TransactionProcessingAndControl = table.Column<bool>(type: "bit", nullable: false),
                    CanBeImpersonated = table.Column<bool>(type: "bit", nullable: false),
                    ApplicationUserId = table.Column<string>(type: "nvarchar(450)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Members", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Members_AspNetUsers_ApplicationUserId",
                        column: x => x.ApplicationUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Members_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Members_Departments_DepartmentId",
                        column: x => x.DepartmentId,
                        principalTable: "Departments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Members_Members_ReportingManagerId",
                        column: x => x.ReportingManagerId,
                        principalTable: "Members",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MemberImpersonationApprovals",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MemberId = table.Column<int>(type: "int", nullable: false),
                    ImpersonatorId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MemberImpersonationApprovals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MemberImpersonationApprovals_Members_ImpersonatorId",
                        column: x => x.ImpersonatorId,
                        principalTable: "Members",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MemberImpersonationApprovals_Members_MemberId",
                        column: x => x.MemberId,
                        principalTable: "Members",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogEntries_OccurredAtUtc",
                table: "AuditLogEntries",
                column: "OccurredAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_Companies_ShortCode",
                table: "Companies",
                column: "ShortCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Departments_Code",
                table: "Departments",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MemberImpersonationApprovals_ImpersonatorId",
                table: "MemberImpersonationApprovals",
                column: "ImpersonatorId");

            migrationBuilder.CreateIndex(
                name: "IX_MemberImpersonationApprovals_MemberId_ImpersonatorId",
                table: "MemberImpersonationApprovals",
                columns: new[] { "MemberId", "ImpersonatorId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Members_ApplicationUserId",
                table: "Members",
                column: "ApplicationUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Members_CompanyId",
                table: "Members",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_Members_DepartmentId",
                table: "Members",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_Members_ReportingManagerId",
                table: "Members",
                column: "ReportingManagerId");

            migrationBuilder.AddForeignKey(
                name: "FK_Transactions_Members_MemberId",
                table: "Transactions",
                column: "MemberId",
                principalTable: "Members",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Transactions_Members_MemberId",
                table: "Transactions");

            migrationBuilder.DropTable(
                name: "AuditLogEntries");

            migrationBuilder.DropTable(
                name: "MemberImpersonationApprovals");

            migrationBuilder.DropTable(
                name: "Members");

            migrationBuilder.DropTable(
                name: "Companies");

            migrationBuilder.DropTable(
                name: "Departments");

            migrationBuilder.RenameColumn(
                name: "MemberId",
                table: "Transactions",
                newName: "EmployeeId");

            migrationBuilder.RenameIndex(
                name: "IX_Transactions_MemberId",
                table: "Transactions",
                newName: "IX_Transactions_EmployeeId");

            migrationBuilder.CreateTable(
                name: "Employees",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FullName = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    IsBoardMember = table.Column<bool>(type: "bit", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Employees", x => x.Id);
                });

            migrationBuilder.AddForeignKey(
                name: "FK_Transactions_Employees_EmployeeId",
                table: "Transactions",
                column: "EmployeeId",
                principalTable: "Employees",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
