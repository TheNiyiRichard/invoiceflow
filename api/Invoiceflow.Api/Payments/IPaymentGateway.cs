namespace Invoiceflow.Api.Payments;

// One card payment at Stripe (a PaymentIntent). Status is Stripe's: requires_payment_method, processing, succeeded, canceled…
public record CardPayment(string Id, string ClientSecret, string Status);

public record CardPaymentRequest(Guid InvoiceId, long AmountCents, string Currency, string Description);

// Anything that takes card payments. Stripe in the app, a fake in tests.
public interface IPaymentGateway
{
    string PublishableKey { get; }
    Task<CardPayment> CreateAsync(CardPaymentRequest request, string idempotencyKey, CancellationToken ct = default);
    Task<CardPayment> GetAsync(string id, CancellationToken ct = default);
}