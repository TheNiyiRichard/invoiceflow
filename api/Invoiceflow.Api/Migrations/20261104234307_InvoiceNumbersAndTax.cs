using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Invoiceflow.Api.Migrations
{
    /// <inheritdoc />
    public partial class InvoiceNumbersAndTax : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Number",
                table: "Invoices",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "TaxRate",
                table: "Invoices",
                type: "decimal(5,4)",
                precision: 5,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            // Number existing invoices per user, oldest first, before the unique index is created
            migrationBuilder.Sql(@"
                WITH n AS (SELECT Number, ROW_NUMBER() OVER (PARTITION BY OwnerId ORDER BY CreatedAt, Id) AS rn FROM Invoices)
                UPDATE n SET Number = rn;");

            // Give existing invoices the 5% rate and their real totals
            migrationBuilder.Sql(@"
                UPDATE i SET TaxRate = 0.05, Subtotal = s.Amount,
                    Tax = ROUND(s.Amount * 0.05, 2), Total = s.Amount + ROUND(s.Amount * 0.05, 2)
                FROM Invoices i
                CROSS APPLY (SELECT ISNULL(SUM(ROUND(li.Quantity * li.UnitPrice, 2)), 0) AS Amount
                             FROM LineItems li WHERE li.InvoiceId = i.Id) s;");

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_OwnerId_Number",
                table: "Invoices",
                columns: new[] { "OwnerId", "Number" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Invoices_OwnerId_Number",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "Number",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "TaxRate",
                table: "Invoices");
        }
    }
}