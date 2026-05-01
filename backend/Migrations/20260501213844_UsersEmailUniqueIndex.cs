using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartCollect.Api.Migrations
{
    /// <inheritdoc />
    public partial class UsersEmailUniqueIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                WITH ranked AS (
                    SELECT ""Id"", ""Email"",
                           ROW_NUMBER() OVER (
                               PARTITION BY lower(""Email"")
                               ORDER BY ""CreatedAt"", ""Id"") AS rn
                    FROM users
                )
                UPDATE users u
                SET ""Email"" = u.""Email"" || '+dup-' || substring(u.""Id""::text, 1, 8),
                    ""Active"" = false
                FROM ranked r
                WHERE u.""Id"" = r.""Id"" AND r.rn > 1;
            ");

            migrationBuilder.Sql(@"DROP INDEX IF EXISTS ""IX_users_TenantId_Email"";");
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS ""IX_users_Master_Email"";");
            migrationBuilder.Sql(@"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_users_Email"" ON users (""Email"");");

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS ""IX_users_Email"";");
            migrationBuilder.Sql(@"
                CREATE UNIQUE INDEX IF NOT EXISTS ""IX_users_TenantId_Email""
                ON users (""TenantId"", ""Email"")
                WHERE ""TenantId"" IS NOT NULL;
            ");
            migrationBuilder.Sql(@"
                CREATE UNIQUE INDEX IF NOT EXISTS ""IX_users_Master_Email""
                ON users (""Email"")
                WHERE ""TenantId"" IS NULL;
            ");

        }
    }
}
