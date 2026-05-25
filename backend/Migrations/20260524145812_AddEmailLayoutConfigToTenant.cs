using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartCollect.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddEmailLayoutConfigToTenant : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "HtmlBody",
                table: "message_templates");

            migrationBuilder.AddColumn<bool>(
                name: "EmailLayoutEnabled",
                table: "tenants",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "EmailLayoutFooterMessage",
                table: "tenants",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EmailLayoutHeroUrl",
                table: "tenants",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EmailLayoutInstagramUrl",
                table: "tenants",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EmailLayoutLinkedInUrl",
                table: "tenants",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EmailLayoutLogoUrl",
                table: "tenants",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EmailLayoutTelegramUrl",
                table: "tenants",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EmailLayoutWhatsAppUrl",
                table: "tenants",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EmailLayoutEnabled",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "EmailLayoutFooterMessage",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "EmailLayoutHeroUrl",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "EmailLayoutInstagramUrl",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "EmailLayoutLinkedInUrl",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "EmailLayoutLogoUrl",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "EmailLayoutTelegramUrl",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "EmailLayoutWhatsAppUrl",
                table: "tenants");

            migrationBuilder.AddColumn<string>(
                name: "HtmlBody",
                table: "message_templates",
                type: "text",
                nullable: true);
        }
    }
}
