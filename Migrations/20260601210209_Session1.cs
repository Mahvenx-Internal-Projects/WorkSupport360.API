using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WorkSupport360.API.Migrations
{
    /// <inheritdoc />
    public partial class Session1 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ClientBudgetType",
                table: "DemoRequests",
                type: "varchar(10)",
                maxLength: 10,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<bool>(
                name: "ClientInterested",
                table: "DemoRequests",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "ClientInterestedAt",
                table: "DemoRequests",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ClientMessage",
                table: "DemoRequests",
                type: "varchar(2000)",
                maxLength: 2000,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<decimal>(
                name: "ClientOfferedBudget",
                table: "DemoRequests",
                type: "decimal(65,30)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "FreelancerInterested",
                table: "DemoRequests",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "FreelancerRespondedAt",
                table: "DemoRequests",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InterestStatus",
                table: "DemoRequests",
                type: "varchar(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "")
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ClientBudgetType",
                table: "DemoRequests");

            migrationBuilder.DropColumn(
                name: "ClientInterested",
                table: "DemoRequests");

            migrationBuilder.DropColumn(
                name: "ClientInterestedAt",
                table: "DemoRequests");

            migrationBuilder.DropColumn(
                name: "ClientMessage",
                table: "DemoRequests");

            migrationBuilder.DropColumn(
                name: "ClientOfferedBudget",
                table: "DemoRequests");

            migrationBuilder.DropColumn(
                name: "FreelancerInterested",
                table: "DemoRequests");

            migrationBuilder.DropColumn(
                name: "FreelancerRespondedAt",
                table: "DemoRequests");

            migrationBuilder.DropColumn(
                name: "InterestStatus",
                table: "DemoRequests");
        }
    }
}
