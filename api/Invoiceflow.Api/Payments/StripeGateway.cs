using Microsoft.Extensions.Options;
using Stripe;

namespace Invoiceflow.Api.Payments;

public class StripeSettings
{
    public string? SecretKey { get; set; }
    public string? PublishableKey { get; set; }
    public string? WebhookSecret { get; set; } // whsec_…, from `stripe listen` (user-secrets)
}

public class StripeGateway(IOptions<StripeSettings> options) : IPaymentGateway
{
    StripeClient? client;

    public string PublishableKey => Settings().PublishableKey!;

    public async Task<CardPayment> CreateAsync(CardPaymentRequest r, string idempotencyKey, CancellationToken ct = default)
    {
        var intent = await Service().CreateAsync(new PaymentIntentCreateOptions
        {
            Amount = r.AmountCents,
            Currency = r.Currency,
            Description = r.Description,
            // Only methods that don't redirect: the payment page (and the token in its URL) never hands off
            // to another site. Cards are always included, and 3-D Secure opens in a pop-up instead.
            AutomaticPaymentMethods = new PaymentIntentAutomaticPaymentMethodsOptions
            {
                Enabled = true,
                AllowRedirects = "never",
            },
            // The webhook (Day 33) uses this to find the invoice. An id only: no names or emails sent to Stripe.
            Metadata = new Dictionary<string, string> { ["invoice_id"] = r.InvoiceId.ToString() },
        }, new RequestOptions { IdempotencyKey = idempotencyKey }, ct);
        return From(intent);
    }

    public async Task<CardPayment> GetAsync(string id, CancellationToken ct = default) =>
        From(await Service().GetAsync(id, cancellationToken: ct));

    static CardPayment From(PaymentIntent p) => new(p.Id, p.ClientSecret, p.Status, p.AmountReceived, p.Currency);

    PaymentIntentService Service() => new(client ??= new StripeClient(Settings().SecretKey));

    // Test mode only until launch: a live key is refused, so a mistake can never charge a real card
    StripeSettings Settings()
    {
        var s = options.Value;
        if (s.SecretKey?.StartsWith("sk_test_") != true || s.PublishableKey?.StartsWith("pk_test_") != true)
            throw new InvalidOperationException("Stripe test keys missing. Set Stripe:SecretKey (sk_test_…) and Stripe:PublishableKey (pk_test_…) with dotnet user-secrets (see README).");
        return s;
    }
}