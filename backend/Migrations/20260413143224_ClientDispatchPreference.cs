using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartCollect.Api.Migrations
{
    /// <inheritdoc />
    public partial class ClientDispatchPreference : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "SendToAllContacts",
                table: "clients",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SendToAllContacts",
                table: "clients");
        }
    }
}
