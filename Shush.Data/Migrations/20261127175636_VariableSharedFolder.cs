using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Shush.Data.Migrations
{
    /// <inheritdoc />
    public partial class VariableSharedFolder : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsSharedFolder",
                table: "Variables",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsSharedFolder",
                table: "Variables");
        }
    }
}
