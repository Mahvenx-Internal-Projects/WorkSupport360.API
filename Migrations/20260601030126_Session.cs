using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WorkSupport360.API.Migrations
{
    /// <inheritdoc />
    public partial class Session : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ActiveSessions",
                table: "Users",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "LoginCount",
                table: "Users",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ActiveSessions",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "LoginCount",
                table: "Users");
        }
    }
}
