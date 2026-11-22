using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Invoiceflow.Api.Migrations
{
    /// <inheritdoc />
    public partial class InvoiceReminders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "LastRemindedAt",
                table: "Invoices",
                type: "datetime2",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastRemindedAt",
                table: "Invoices");
        }
    }
}
