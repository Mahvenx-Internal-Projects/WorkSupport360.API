using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WorkSupport360.API.Migrations
{
    /// <inheritdoc />
    public partial class SupportAgentFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "AssignedAgentId",
                table: "SupportTickets",
                type: "char(36)",
                nullable: true,
                collation: "ascii_general_ci");

            migrationBuilder.AddColumn<string>(
                name: "AssignedAgentName",
                table: "SupportTickets",
                type: "varchar(100)",
                maxLength: 100,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<DateTime>(
                name: "AssignedAt",
                table: "SupportTickets",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BotSummary",
                table: "SupportTickets",
                type: "varchar(500)",
                maxLength: 500,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "ContactEmail",
                table: "SupportTickets",
                type: "varchar(100)",
                maxLength: 100,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "ContactPhone",
                table: "SupportTickets",
                type: "varchar(20)",
                maxLength: 20,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<bool>(
                name: "IsRead",
                table: "SupportTickets",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastMessageAt",
                table: "SupportTickets",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UserType",
                table: "SupportTickets",
                type: "varchar(50)",
                maxLength: 50,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AssignedAgentId",
                table: "SupportTickets");

            migrationBuilder.DropColumn(
                name: "AssignedAgentName",
                table: "SupportTickets");

            migrationBuilder.DropColumn(
                name: "AssignedAt",
                table: "SupportTickets");

            migrationBuilder.DropColumn(
                name: "BotSummary",
                table: "SupportTickets");

            migrationBuilder.DropColumn(
                name: "ContactEmail",
                table: "SupportTickets");

            migrationBuilder.DropColumn(
                name: "ContactPhone",
                table: "SupportTickets");

            migrationBuilder.DropColumn(
                name: "IsRead",
                table: "SupportTickets");

            migrationBuilder.DropColumn(
                name: "LastMessageAt",
                table: "SupportTickets");

            migrationBuilder.DropColumn(
                name: "UserType",
                table: "SupportTickets");
        }
    }
}
