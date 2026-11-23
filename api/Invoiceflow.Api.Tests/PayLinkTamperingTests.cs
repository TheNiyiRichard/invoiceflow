using System.Buffers.Binary;
using System.Buffers.Text;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Invoiceflow.Api.Models;
using Invoiceflow.Api.Payments;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Invoiceflow.Api.Tests;

// Every public route that takes a payment link gets every kind of bad link.
// Each one must be refused, say nothing useful, and change nothing.
[Collection("api")]
public class PayLinkTamperingTests(ApiFactory factory)
{
    // Same message for every bad link, so a guesser learns nothing
    const string NotValid = "This payment link isn't valid.";

    // Every route with {token} in it, found the same way EndpointAuthTests finds routes
    List<(string Method, string Path)> PayRoutes() =>
        factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText?.Contains("{token}") == true)
            .SelectMany(e => (e.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods ?? new[] { "GET" })
                .Select(method => (method, "/" + e.RoutePattern.RawText!.TrimStart('/'))))
            .Distinct()
            .ToList();

    // Decode a real link, change some bytes, encode it again. The old signature stays.
    static string Rewrite(string token, Action<byte[]> change)
    {
        var bytes = Base64Url.DecodeFromChars(token);
        change(bytes);
        return Base64Url.EncodeToString(bytes);
    }

    [Theory]
    [InlineData("expired")]
    [InlineData("one-character-changed")]
    [InlineData("pointed-at-another-invoice")]
    [InlineData("expiry-pushed-back")]
    [InlineData("signed-with-another-key")]
    [InlineData("not-a-link")]
    public async Task Bad_links_are_refused_on_every_pay_route(string attack)
    {
        var a = await factory.SignedInUser("a");
        var client = await a.AddClient();
        var target = await a.AddInvoice(client.Id);
        var other = await a.AddInvoice(client.Id);
        await factory.SetStatus(target.Id, InvoiceStatus.Sent);
        await factory.SetStatus(other.Id, InvoiceStatus.Sent);

        var links = factory.Services.GetRequiredService<PaymentLinks>();
        var valid = links.Create(target.Id, DateTimeOffset.UtcNow.AddDays(30));
        var expired = links.Create(target.Id, DateTimeOffset.UtcNow.AddMinutes(-1));

        var (token, expected) = attack switch
        {
            // Really signed, just out of date: the only case that's allowed to say so
            "expired" => (expired, HttpStatusCode.Gone),
            "one-character-changed" => (valid[..40] + (valid[40] == 'A' ? 'B' : 'A') + valid[41..], HttpStatusCode.NotFound),
            // Someone swaps in the id of a different invoice, keeping the old signature
            "pointed-at-another-invoice" => (Rewrite(valid, b => other.Id.TryWriteBytes(b.AsSpan(1, 16))), HttpStatusCode.NotFound),
            // Someone revives an expired link by moving its date a year on
            "expiry-pushed-back" => (Rewrite(expired, b => BinaryPrimitives.WriteInt64BigEndian(b.AsSpan(17, 8),
                DateTimeOffset.UtcNow.AddYears(1).ToUnixTimeSeconds())), HttpStatusCode.NotFound),
            "signed-with-another-key" => (new PaymentLinks(RandomNumberGenerator.GetBytes(32), "https://x", TimeProvider.System)
                .Create(target.Id, DateTimeOffset.UtcNow.AddDays(30)), HttpStatusCode.NotFound),
            _ => ("not-a-real-payment-link", HttpStatusCode.NotFound),
        };

        var routes = PayRoutes();
        Assert.True(routes.Count >= 3, "Route discovery found too few pay routes"); // never pass by finding nothing

        var visitor = factory.NewBrowser(); // signed out, like anyone holding a link
        var failures = new List<string>();
        foreach (var (method, path) in routes)
        {
            var res = await visitor.SendAsync(new HttpRequestMessage(new HttpMethod(method), path.Replace("{token}", token)));
            if (res.StatusCode != expected)
                failures.Add($"{method} {path} answered {(int)res.StatusCode}");
            else if (expected == HttpStatusCode.NotFound
                && (await res.Content.ReadFromJsonAsync<ProblemDetails>())?.Title != NotValid)
                failures.Add($"{method} {path} gave a different 404 message");
        }
        Assert.Empty(failures);

        // Nothing changed: neither invoice was marked Viewed, and no card payment was started
        Assert.Equal("Sent", await a.StatusOf(target.Id));
        Assert.Equal("Sent", await a.StatusOf(other.Id));
        Assert.Empty(factory.Payments.For(target.Id));
        Assert.Empty(factory.Payments.For(other.Id));
    }
}