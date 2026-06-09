using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartCollect.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddDispatchWindowDays : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<List<int>>(
                name: "DispatchWindowDays",
                table: "tenants",
                type: "integer[]",
                nullable: false,
                defaultValue: new List<int> { 1, 2, 3, 4, 5 });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DispatchWindowDays",
                table: "tenants");
        }
    }
}
