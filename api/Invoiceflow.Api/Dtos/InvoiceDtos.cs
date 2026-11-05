using System.ComponentModel.DataAnnotations;

namespace Invoiceflow.Api.Dtos;

public record LineItemDto(Guid Id, int Position, string Description, decimal Quantity, decimal UnitPrice, decimal Amount);

public record InvoiceDto(
    Guid Id, string Number, Guid ClientId, string ClientName, string Status,
    DateOnly IssueDate, DateOnly DueDate, string? Notes,
    decimal Subtotal, decimal TaxRate, decimal Tax, decimal Total,
    List<LineItemDto> LineItems, DateTime CreatedAt, DateTime UpdatedAt);

public record InvoiceSummaryDto(Guid Id, string Number, Guid ClientId, string ClientName, string Status, DateOnly IssueDate, DateOnly DueDate, decimal Total);

public record PagedResult<T>(List<T> Items, int Page, int PageSize, int TotalCount);

// What the API accepts. No OwnerId, Number, Status, rate or totals: the server decides those.
public class InvoiceInput
{
    [Required]
    public Guid? ClientId { get; set; }

    [Required]
    public DateOnly? IssueDate { get; set; }

    [Required]
    public DateOnly? DueDate { get; set; }

    [StringLength(2000)]
    public string? Notes { get; set; }

    [Required, MinLength(1, ErrorMessage = "Add at least one line item."), MaxLength(100)]
    public List<LineItemInput> LineItems { get; set; } = [];
}

public class LineItemInput
{
    [Required, StringLength(500)]
    public string Description { get; set; } = "";

    [Range(0.01, 9_999_999.99)]
    public decimal Quantity { get; set; }

    [Range(0, 1_000_000_000)]
    public decimal UnitPrice { get; set; }
}