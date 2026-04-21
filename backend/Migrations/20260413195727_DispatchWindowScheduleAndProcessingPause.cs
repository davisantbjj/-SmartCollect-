using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartCollect.Api.Migrations
{
    /// <inheritdoc />
    public partial class DispatchWindowScheduleAndProcessingPause : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "DispatchWindowEnabled",
                table: "tenants",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "DispatchWindowEndMinutes",
                table: "tenants",
                type: "integer",
                nullable: false,
                defaultValue: 1080);

            migrationBuilder.AddColumn<int>(
                name: "DispatchWindowStartMinutes",
                table: "tenants",
                type: "integer",
                nullable: false,
                defaultValue: 540);

            migrationBuilder.AddColumn<string>(
                name: "DispatchWindowTimeZone",
                table: "tenants",
                type: "text",
                nullable: false,
                defaultValue: "UTC");

            migrationBuilder.AddColumn<bool>(
                name: "PauseAutomaticDispatchDuringProcessing",
                table: "tenants",
                type: "boolean",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DispatchWindowEnabled",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "DispatchWindowEndMinutes",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "DispatchWindowStartMinutes",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "DispatchWindowTimeZone",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "PauseAutomaticDispatchDuringProcessing",
                table: "tenants");
        }
    }
}
