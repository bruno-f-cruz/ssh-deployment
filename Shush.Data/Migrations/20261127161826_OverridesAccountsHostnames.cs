using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Shush.Data.Migrations
{
    /// <inheritdoc />
    public partial class OverridesAccountsHostnames : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "OverridesJson",
                table: "Runs",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "CopiedFrom",
                table: "Recipes",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Hostname",
                table: "Machines",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OverridesJson",
                table: "Deployments",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "SshUser",
                table: "Deployments",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "OverridesJson",
                table: "Runs");

            migrationBuilder.DropColumn(
                name: "CopiedFrom",
                table: "Recipes");

            migrationBuilder.DropColumn(
                name: "Hostname",
                table: "Machines");

            migrationBuilder.DropColumn(
                name: "OverridesJson",
                table: "Deployments");

            migrationBuilder.DropColumn(
                name: "SshUser",
                table: "Deployments");
        }
    }
}
