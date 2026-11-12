namespace Invoiceflow.Api.Models;

public class Invoice
{
    public Guid Id { get; set; }

    // Per-user sequence, shown as INV-0001. Set by the server; the database refuses duplicates.
    public int Number { get; set; }

    // Set by the server from the sign-in cookie, never from the request body
    public string OwnerId { get; set; } = "";

    public Guid ClientId { get; set; }
    public Client Client { get; set; } = null!;

    public InvoiceStatus Status { get; set; } = InvoiceStatus.Draft;
    public DateOnly IssueDate { get; set; }
    public DateOnly DueDate { get; set; }
    public string? Notes { get; set; }

    // Money is decimal, never float or double. All calculated on the server by InvoiceMath.
    // The rate is saved on each invoice, so a future rate change never alters old invoices.
    public decimal TaxRate { get; set; }
    public decimal Subtotal { get; set; }
    public decimal Tax { get; set; }
    public decimal Total { get; set; }
    
    // When it was paid (UTC). Set by the payment code in Week 5; null until then.
    public DateTime? PaidAt { get; set; }
    public List<LineItem> LineItems { get; set; } = [];

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}