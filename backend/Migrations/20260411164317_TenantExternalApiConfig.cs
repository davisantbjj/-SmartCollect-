using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartCollect.Api.Migrations
{
    /// <inheritdoc />
    public partial class TenantExternalApiConfig : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ExternalApiAuthScheme",
                table: "tenants",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExternalApiBaseUrl",
                table: "tenants",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExternalApiDocsUrl",
                table: "tenants",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExternalApiOccurrencesPath",
                table: "tenants",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExternalApiPendingTitlesPath",
                table: "tenants",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExternalApiTokenEncrypted",
                table: "tenants",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ExternalApiAuthScheme",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "ExternalApiBaseUrl",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "ExternalApiDocsUrl",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "ExternalApiOccurrencesPath",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "ExternalApiPendingTitlesPath",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "ExternalApiTokenEncrypted",
                table: "tenants");
        }
    }
}
