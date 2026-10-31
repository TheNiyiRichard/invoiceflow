using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Invoiceflow.Api.Migrations
{
    /// <inheritdoc />
    public partial class ClientOwner : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Old development clients have no owner, so the new foreign key would reject them
            migrationBuilder.Sql("DELETE FROM [Clients];");

            migrationBuilder.DeleteData(
                table: "Clients",
                keyColumn: "Id",
                keyValue: new Guid("3f1c2a9e-6b4d-4e8a-9c71-2d5e8f0a1b01"));

            migrationBuilder.DeleteData(
                table: "Clients",
                keyColumn: "Id",
                keyValue: new Guid("8a7e5d21-0c3b-4f69-b2a4-71e9c6d3f802"));

            migrationBuilder.DeleteData(
                table: "Clients",
                keyColumn: "Id",
                keyValue: new Guid("c94b0f37-2e18-4d5a-8f60-e3a1b7d49c03"));

            migrationBuilder.AddColumn<string>(
                name: "OwnerId",
                table: "Clients",
                type: "nvarchar(450)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_Clients_OwnerId",
                table: "Clients",
                column: "OwnerId");

            migrationBuilder.AddForeignKey(
                name: "FK_Clients_AspNetUsers_OwnerId",
                table: "Clients",
                column: "OwnerId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Clients_AspNetUsers_OwnerId",
                table: "Clients");

            migrationBuilder.DropIndex(
                name: "IX_Clients_OwnerId",
                table: "Clients");

            migrationBuilder.DropColumn(
                name: "OwnerId",
                table: "Clients");

            migrationBuilder.InsertData(
                table: "Clients",
                columns: new[] { "Id", "Address", "Company", "CreatedAt", "Email", "Name" },
                values: new object[,]
                {
                    { new Guid("3f1c2a9e-6b4d-4e8a-9c71-2d5e8f0a1b01"), "12 Allen Avenue, Ikeja, Lagos", "Okafor Studio", new DateTime(2026, 10, 23, 0, 0, 0, 0, DateTimeKind.Utc), "amara@example.com", "Amara Okafor" },
                    { new Guid("8a7e5d21-0c3b-4f69-b2a4-71e9c6d3f802"), "4 Oxford Street, Accra", "Mensah Logistics", new DateTime(2026, 10, 23, 0, 0, 0, 0, DateTimeKind.Utc), "daniel@example.com", "Daniel Mensah" },
                    { new Guid("c94b0f37-2e18-4d5a-8f60-e3a1b7d49c03"), "27 Market Lane, Leeds", null, new DateTime(2026, 10, 23, 0, 0, 0, 0, DateTimeKind.Utc), "sarah@example.com", "Sarah Whitfield" }
                });
        }
    }
}