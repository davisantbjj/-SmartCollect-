using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using SmartCollect.Infrastructure.Data;

#nullable disable

namespace SmartCollect.Api.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(AppDbContext))]
    [Migration("20260410000001_RolesAndDispatchStatus")]
    public partial class RolesAndDispatchStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1. Make users.TenantId nullable (Master users have no tenant)
            migrationBuilder.DropForeignKey(
                name: "FK_users_tenants_TenantId",
                table: "users");

            migrationBuilder.AlterColumn<Guid>(
                name: "TenantId",
                table: "users",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: false);

            // 2. Drop old unique index, recreate as partial (only for non-null TenantId)
            migrationBuilder.DropIndex(
                name: "IX_users_TenantId_Email",
                table: "users");

            migrationBuilder.Sql(@"
                CREATE UNIQUE INDEX ""IX_users_TenantId_Email""
                ON users (""TenantId"", ""Email"")
                WHERE ""TenantId"" IS NOT NULL;
            ");

            // Also unique index for Master users (TenantId IS NULL, unique by email)
            migrationBuilder.Sql(@"
                CREATE UNIQUE INDEX ""IX_users_Master_Email""
                ON users (""Email"")
                WHERE ""TenantId"" IS NULL;
            ");

            // 3. Re-add FK as optional
            migrationBuilder.AddForeignKey(
                name: "FK_users_tenants_TenantId",
                table: "users",
                column: "TenantId",
                principalTable: "tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            // 4. Rename enum value: Operator → Worker in existing data
            migrationBuilder.Sql(@"UPDATE users SET ""Role"" = 'Worker' WHERE ""Role"" = 'Operator';");
            migrationBuilder.Sql(@"UPDATE users SET ""Role"" = 'Worker' WHERE ""Role"" = 'Manager';");

            // 5. No SQL needed for DispatchStatus.Cancelled — stored as string, new value just starts being used
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Revert Cancelled dispatches back to Error
            migrationBuilder.Sql(@"UPDATE dispatches SET ""Status"" = 'Error' WHERE ""Status"" = 'Cancelled';");

            // Revert Worker back to Operator
            migrationBuilder.Sql(@"UPDATE users SET ""Role"" = 'Operator' WHERE ""Role"" = 'Worker';");

            // Drop partial indexes
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS ""IX_users_TenantId_Email"";");
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS ""IX_users_Master_Email"";");

            // Drop optional FK
            migrationBuilder.DropForeignKey(
                name: "FK_users_tenants_TenantId",
                table: "users");

            // Make TenantId not nullable again
            migrationBuilder.AlterColumn<Guid>(
                name: "TenantId",
                table: "users",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            // Restore old unique index
            migrationBuilder.CreateIndex(
                name: "IX_users_TenantId_Email",
                table: "users",
                columns: new[] { "TenantId", "Email" },
                unique: true);

            // Restore original FK
            migrationBuilder.AddForeignKey(
                name: "FK_users_tenants_TenantId",
                table: "users",
                column: "TenantId",
                principalTable: "tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
