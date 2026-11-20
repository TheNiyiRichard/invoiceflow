using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Invoiceflow.Api.Migrations
{
    /// <inheritdoc />
    public partial class InvoicePaymentIntent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "StripePaymentIntentId",
                table: "Invoices",
                type: "nvarchar(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_StripePaymentIntentId",
                table: "Invoices",
                column: "StripePaymentIntentId",
                unique: true,
                filter: "[StripePaymentIntentId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Invoices_StripePaymentIntentId",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "StripePaymentIntentId",
                table: "Invoices");
        }
    }
}
