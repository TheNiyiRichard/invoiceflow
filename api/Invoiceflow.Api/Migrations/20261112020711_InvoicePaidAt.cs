using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Invoiceflow.Api.Migrations
{
    /// <inheritdoc />
    public partial class InvoicePaidAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "PaidAt",
                table: "Invoices",
                type: "datetime2",
                nullable: true);
            
            // Invoices you marked Paid in SSMS while testing get a date, so they show up in the stats
            migrationBuilder.Sql("UPDATE [Invoices] SET [PaidAt] = [UpdatedAt] WHERE [Status] = 'Paid';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PaidAt",
                table: "Invoices");
        }
    }
}
