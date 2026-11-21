using Microsoft.Extensions.Options;
using Stripe;

namespace Invoiceflow.Api.Payments;

// What a verified "payment succeeded" event says. Read only after the signature checks out.
public record PaymentSucceeded(string EventId, string IntentId, Guid? InvoiceId, long AmountCents, string Currency, DateTime PaidAt);

public class StripeWebhooks(IOptions<StripeSettings> options, ILogger<StripeWebhooks> logger)
{
    // A signature older than 5 minutes is treated as a replay
    const long ToleranceSeconds = 300;

    // The Stripe event, or null if the signature is missing, wrong, or too old
    public Event? Verify(string json, string signature)
    {
        var secret = options.Value.WebhookSecret;
        if (secret?.StartsWith("whsec_") != true)
            throw new InvalidOperationException("Stripe:WebhookSecret missing. Run `stripe listen` and set the whsec_… it prints with dotnet user-secrets (see README).");
        if (string.IsNullOrWhiteSpace(signature)) return null;

        try
        {
            // HMAC-SHA256 over the exact text received, with this endpoint's secret, and the signed time must be recent.
            // false = don't fail on API version: the Stripe CLI can send a newer one, and only long-stable fields are read.
            return EventUtility.ConstructEvent(json, signature, secret, ToleranceSeconds, false);
        }
        catch (Exception ex)
        {
            // Anything that doesn't verify is rejected. Only the reason is logged, never the body or the header.
            logger.LogWarning("Stripe webhook rejected: {Reason}", ex.Message);
            return null;
        }
    }

    // A payment Stripe says succeeded, or null for every other kind of event
    public static PaymentSucceeded? Succeeded(Event e)
    {
        if (e.Type != "payment_intent.succeeded" || e.Data?.Object is not PaymentIntent p) return null;
        Guid? invoiceId = p.Metadata is not null && p.Metadata.TryGetValue("invoice_id", out var raw) && Guid.TryParse(raw, out var id) ? id : null;
        return new PaymentSucceeded(e.Id, p.Id, invoiceId, p.AmountReceived, p.Currency, e.Created);
    }
}