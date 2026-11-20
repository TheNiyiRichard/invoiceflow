namespace Invoiceflow.Api.Dtos;

// What the owner gets back: the link to send, and when it stops working
public record PaymentLinkDto(string Url, DateTimeOffset ExpiresAt);

// What anyone holding the link sees: enough to pay, nothing more. No ids, emails or addresses.
// Day 31 adds the line items for the payment page.
// What anyone holding the link sees: enough to pay, nothing more. No ids, emails or addresses.
// What anyone holding the link sees: enough to pay, nothing more. No ids, emails or addresses.
public record PaymentPageDto(
    string Number, string From, string Status, DateOnly IssueDate, DateOnly DueDate,
    List<PaymentLineDto> Lines, decimal Subtotal, decimal TaxRate, decimal Tax, decimal Total);

public record PaymentLineDto(int Position, string Description, decimal Quantity, decimal UnitPrice, decimal Amount);

// What the payment page needs to show Stripe's card form. The client secret lets this browser confirm
// this one payment only: it can't change the amount or see any other payment.
public record CardPaymentDto(string ClientSecret, string PublishableKey);