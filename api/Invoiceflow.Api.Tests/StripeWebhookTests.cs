using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Invoiceflow.Api.Dtos;
using Invoiceflow.Api.Models;
using Invoiceflow.Api.Payments;
using Microsoft.Extensions.DependencyInjection;

namespace Invoiceflow.Api.Tests;

// Webhooks signed the way Stripe signs them, sent to the real endpoint, checked by the real Stripe library
[Collection("api")]
public class StripeWebhookTests(ApiFactory factory)
{
    record Started(HttpClient Owner, Guid InvoiceId, string Token, string IntentId, long Cents);

    // A sent invoice whose client has opened the card form, so a payment exists at "Stripe"
    async Task<Started> PaymentStarted()
    {
        var a = await factory.SignedInUser("a");
        var client = await a.AddClient();
        var invoice = await a.AddInvoice(client.Id); // 315.00
        await factory.SetStatus(invoice.Id, InvoiceStatus.Sent);
        var token = factory.Services.GetRequiredService<PaymentLinks>().Create(invoice.Id, DateTimeOffset.UtcNow.AddDays(30));
        Assert.Equal(HttpStatusCode.OK, (await factory.NewBrowser().PostAsync($"/api/pay/{token}/intent", null)).StatusCode);
        var intent = factory.Payments.For(invoice.Id).Single();
        return new Started(a, invoice.Id, token, intent.Id, intent.Request.AmountCents);
    }

    static string Succeeded(string intentId, Guid invoiceId, long cents, string currency = "cad") => JsonSerializer.Serialize(new
    {
        id = $"evt_test_{Guid.NewGuid():N}",
        @object = "event",
        api_version = "2025-01-27.acacia",
        created = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
        livemode = false,
        type = "payment_intent.succeeded",
        data = new
        {
            @object = new
            {
                id = intentId,
                @object = "payment_intent",
                amount = cents,
                amount_received = cents,
                currency,
                status = "succeeded",
                metadata = new { invoice_id = invoiceId.ToString() },
            },
        },
    });

    // Stripe's scheme: "t=<unix time>,v1=<hex HMAC-SHA256 of "<t>.<body>" with the endpoint's secret>"
    static string Sign(string json, string secret, DateTimeOffset? at = null)
    {
        var t = (at ?? DateTimeOffset.UtcNow).ToUnixTimeSeconds();
        var mac = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes($"{t}.{json}"));
        return $"t={t},v1={Convert.ToHexStringLower(mac)}";
    }

    Task<HttpResponseMessage> Deliver(string json, string? signature)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/webhooks/stripe")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        if (signature is not null) request.Headers.TryAddWithoutValidation("Stripe-Signature", signature);
        return factory.NewBrowser().SendAsync(request);
    }

    [Fact]
    public async Task A_verified_payment_marks_the_invoice_paid_once()
    {
        var p = await PaymentStarted();
        var json = Succeeded(p.IntentId, p.InvoiceId, p.Cents);

        Assert.Equal(HttpStatusCode.OK, (await Deliver(json, Sign(json, factory.WebhookSecret))).StatusCode);
        Assert.Equal("Paid", await p.Owner.StatusOf(p.InvoiceId));

        // Stripe sometimes delivers the same event twice: still 200, nothing changes
        Assert.Equal(HttpStatusCode.OK, (await Deliver(json, Sign(json, factory.WebhookSecret))).StatusCode);
        Assert.Equal("Paid", await p.Owner.StatusOf(p.InvoiceId));

        // The client's page says Paid, and the card form can't start again
        var page = await factory.NewBrowser().GetFromJsonAsync<PaymentPageDto>($"/api/pay/{p.Token}");
        Assert.Equal("Paid", page!.Status);
        Assert.Equal(HttpStatusCode.Conflict, (await factory.NewBrowser().PostAsync($"/api/pay/{p.Token}/intent", null)).StatusCode);
    }

    [Theory]
    [InlineData("wrong-secret")]
    [InlineData("no-signature")]
    [InlineData("changed-after-signing")]
    [InlineData("replayed-10-minutes-later")]
    public async Task Unverified_webhooks_are_rejected(string attack)
    {
        var p = await PaymentStarted();
        var json = Succeeded(p.IntentId, p.InvoiceId, p.Cents);

        var res = attack switch
        {
            "wrong-secret" => await Deliver(json, Sign(json, "whsec_someone_else")),
            "no-signature" => await Deliver(json, null),
            "changed-after-signing" => await Deliver(json.Replace(p.IntentId, "pi_attacker"), Sign(json, factory.WebhookSecret)),
            _ => await Deliver(json, Sign(json, factory.WebhookSecret, DateTimeOffset.UtcNow.AddMinutes(-10))),
        };

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal("Sent", await p.Owner.StatusOf(p.InvoiceId));
    }

    [Theory]
    [InlineData("one-cent-short")]
    [InlineData("usd")]
    [InlineData("other-invoice")]
    public async Task A_genuine_event_that_does_not_match_the_invoice_changes_nothing(string mismatch)
    {
        var p = await PaymentStarted();
        var json = mismatch switch
        {
            "one-cent-short" => Succeeded(p.IntentId, p.InvoiceId, p.Cents - 1),
            "usd" => Succeeded(p.IntentId, p.InvoiceId, p.Cents, "usd"),
            _ => Succeeded(p.IntentId, Guid.NewGuid(), p.Cents),
        };

        // Signed properly, so Stripe gets its 200 and stops retrying. But the invoice isn't paid.
        Assert.Equal(HttpStatusCode.OK, (await Deliver(json, Sign(json, factory.WebhookSecret))).StatusCode);
        Assert.Equal("Sent", await p.Owner.StatusOf(p.InvoiceId));
    }
}