using System.Collections.Concurrent;
using Invoiceflow.Api.Payments;

namespace Invoiceflow.Api.Tests;

// Stands in for Stripe, with the same rule: the same idempotency key returns the same payment
public class FakePayments : IPaymentGateway
{
    public record Intent(string Id, CardPaymentRequest Request)
    {
        public string Status { get; set; } = "requires_payment_method";
        public string ClientSecret => $"{Id}_secret_fake";
    }

    readonly ConcurrentDictionary<string, Intent> byKey = new();
    readonly ConcurrentDictionary<string, Intent> byId = new();

    public string PublishableKey => "pk_test_fake";

    public Task<CardPayment> CreateAsync(CardPaymentRequest request, string idempotencyKey, CancellationToken ct = default)
    {
        var intent = byKey.GetOrAdd(idempotencyKey, _ => new Intent($"pi_fake_{Guid.NewGuid():N}", request));
        byId[intent.Id] = intent;
        return Task.FromResult(ToPayment(intent));
    }

    public Task<CardPayment> GetAsync(string id, CancellationToken ct = default) => Task.FromResult(ToPayment(byId[id]));

    // Every payment created for one invoice
    public List<Intent> For(Guid invoiceId) => byId.Values.Where(i => i.Request.InvoiceId == invoiceId).ToList();

    static CardPayment ToPayment(Intent i) => new(i.Id, i.ClientSecret, i.Status);
}