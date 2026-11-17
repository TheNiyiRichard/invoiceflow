namespace Invoiceflow.Api.Dtos;

// What the owner gets back: the link to send, and when it stops working
public record PaymentLinkDto(string Url, DateTimeOffset ExpiresAt);

// What anyone holding the link sees: enough to pay, nothing more. No ids, emails or addresses.
// Day 31 adds the line items for the payment page.
public record PaymentPageDto(string Number, string From, string Status, DateOnly DueDate, decimal Total);