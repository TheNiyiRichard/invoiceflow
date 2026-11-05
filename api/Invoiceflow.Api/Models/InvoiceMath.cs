namespace Invoiceflow.Api.Models;

// All the money maths in one place. decimal only: in double, 0.1 × 3 is 0.30000000000000004.
public static class InvoiceMath
{
    public const decimal GstRate = 0.05m;

    // Big enough for any freelancer, and well inside what decimal(18,2) can store, tax included
    public const decimal MaxSubtotal = 1_000_000_000m;

    // Invoices round halves up: 2.345 → 2.35. C#'s default (banker's rounding) would give 2.34.
    public static decimal Round(decimal amount) => Math.Round(amount, 2, MidpointRounding.AwayFromZero);

    public static decimal LineAmount(decimal quantity, decimal unitPrice) => Round(quantity * unitPrice);

    // Round each line first, then add, so the lines on the invoice add up exactly to the subtotal
    public static void Recalculate(Invoice invoice)
    {
        invoice.Subtotal = invoice.LineItems.Sum(li => LineAmount(li.Quantity, li.UnitPrice));
        invoice.Tax = Round(invoice.Subtotal * invoice.TaxRate);
        invoice.Total = invoice.Subtotal + invoice.Tax;
    }

    public static string FormatNumber(int number) => $"INV-{number:D4}";
}