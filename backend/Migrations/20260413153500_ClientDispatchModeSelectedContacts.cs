using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartCollect.Api.Migrations
{
    /// <inheritdoc />
    public partial class ClientDispatchModeSelectedContacts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DispatchMode",
                table: "clients",
                type: "text",
                nullable: false,
                defaultValue: "Primary");

            migrationBuilder.AddColumn<string>(
                name: "SelectedDispatchContactIdsJson",
                table: "clients",
                type: "text",
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE clients
                SET "DispatchMode" = CASE
                    WHEN "SendToAllContacts" = TRUE THEN 'All'
                    ELSE 'Primary'
                END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DispatchMode",
                table: "clients");

            migrationBuilder.DropColumn(
                name: "SelectedDispatchContactIdsJson",
                table: "clients");
        }
    }
}
