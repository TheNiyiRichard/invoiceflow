namespace Invoiceflow.Api.Models;

public class Invoice
{
    public Guid Id { get; set; }

    // Set by the server from the sign-in cookie, never from the request body
    public string OwnerId { get; set; } = "";

    public Guid ClientId { get; set; }
    public Client Client { get; set; } = null!;

    public InvoiceStatus Status { get; set; } = InvoiceStatus.Draft;
    public DateOnly IssueDate { get; set; }
    public DateOnly DueDate { get; set; }
    public string? Notes { get; set; }

    // Money is decimal, never float or double. Calculated on the server (Day 17).
    public decimal Subtotal { get; set; }
    public decimal Tax { get; set; }
    public decimal Total { get; set; }

    public List<LineItem> LineItems { get; set; } = [];

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}