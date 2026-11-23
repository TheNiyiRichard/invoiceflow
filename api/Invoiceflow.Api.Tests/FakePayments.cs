using System.Collections.Concurrent;
using Invoiceflow.Api.Payments;

namespace Invoiceflow.Api.Tests;

// Stands in for Stripe, with the same rule: the same idempotency key returns the same payment
public class FakePayments : IPaymentGateway
{
    public record Intent(string Id, CardPaymentRequest Request)
    {
        public string Status { get; set; } = "requires_payment_method";
        public long AmountReceived { get; set; }
        public bool Unreachable { get; set; } // looking it up fails, as if Stripe is down
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

    public Task<CardPayment> GetAsync(string id, CancellationToken ct = default)
    {
        var intent = byId[id];
        if (intent.Unreachable) throw new HttpRequestException("Stripe unavailable (test)");
        return Task.FromResult(ToPayment(intent));
    }

    // What Stripe does when the client's card goes through
    public void Succeed(string id)
    {
        var intent = byId[id];
        intent.Status = "succeeded";
        intent.AmountReceived = intent.Request.AmountCents;
    }

    // Every payment created for one invoice
    public List<Intent> For(Guid invoiceId) => byId.Values.Where(i => i.Request.InvoiceId == invoiceId).ToList();

    static CardPayment ToPayment(Intent i) => new(i.Id, i.ClientSecret, i.Status, i.AmountReceived, i.Request.Currency);
}