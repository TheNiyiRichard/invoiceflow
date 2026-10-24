using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Invoiceflow.Api.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Clients",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Email = table.Column<string>(type: "nvarchar(254)", maxLength: 254, nullable: false),
                    Company = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Address = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Clients", x => x.Id);
                });

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

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Clients");
        }
    }
}
