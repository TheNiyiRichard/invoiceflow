using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Invoiceflow.Api.Dtos;
using Invoiceflow.Api.Models;
using Invoiceflow.Api.Payments;
using Microsoft.Extensions.DependencyInjection;

namespace Invoiceflow.Api.Tests;

// The real endpoints: the owner makes a link, a signed-out visitor opens it
[Collection("api")]
public class PaymentLinkTests(ApiFactory factory)
{
    static string TokenOf(string url) => url[(url.LastIndexOf('/') + 1)..];

    [Fact]
    public async Task A_signed_link_opens_the_invoice_while_signed_out()
    {
        var a = await factory.SignedInUser("a");
        var client = await a.AddClient();
        var invoice = await a.AddInvoice(client.Id);
        await factory.SetStatus(invoice.Id, InvoiceStatus.Sent);

        var res = await a.PostAsync($"/api/invoices/{invoice.Id}/payment-link", null);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var link = (await res.Content.ReadFromJsonAsync<PaymentLinkDto>())!;
        Assert.InRange(link.ExpiresAt, DateTimeOffset.UtcNow.AddDays(29), DateTimeOffset.UtcNow.AddDays(31));

        var visitor = factory.NewBrowser(); // signed out, like the client opening the email
        var page = await visitor.GetAsync($"/api/pay/{TokenOf(link.Url)}");
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.True(page.Headers.CacheControl?.NoStore);

        var body = (await page.Content.ReadFromJsonAsync<PaymentPageDto>())!;
        Assert.Equal(invoice.Number, body.Number);
        Assert.Equal(invoice.Total, body.Total);
        Assert.Equal("Tester a", body.From);
    }

    [Fact]
    public async Task Drafts_and_paid_invoices_get_no_link()
    {
        var a = await factory.SignedInUser("a");
        var client = await a.AddClient();

        var draft = await a.AddInvoice(client.Id);
        Assert.Equal(HttpStatusCode.Conflict, (await a.PostAsync($"/api/invoices/{draft.Id}/payment-link", null)).StatusCode);

        var paid = await a.AddInvoice(client.Id);
        await factory.MarkPaid(paid.Id, new DateOnly(2026, 11, 3), DateTime.UtcNow);
        Assert.Equal(HttpStatusCode.Conflict, (await a.PostAsync($"/api/invoices/{paid.Id}/payment-link", null)).StatusCode);
    }

    [Fact]
    public async Task Expired_forged_and_draft_links_show_nothing()
    {
        var a = await factory.SignedInUser("a");
        var client = await a.AddClient();
        var sent = await a.AddInvoice(client.Id);
        await factory.SetStatus(sent.Id, InvoiceStatus.Sent);
        var draft = await a.AddInvoice(client.Id);

        var links = factory.Services.GetRequiredService<PaymentLinks>();
        var visitor = factory.NewBrowser();
        async Task<HttpStatusCode> Open(string token) => (await visitor.GetAsync($"/api/pay/{token}")).StatusCode;

        // Really signed, but out of date
        Assert.Equal(HttpStatusCode.Gone, await Open(links.Create(sent.Id, DateTimeOffset.UtcNow.AddMinutes(-1))));

        // Right invoice, wrong key: what an attacker without the key would have to try
        var attacker = new PaymentLinks(RandomNumberGenerator.GetBytes(32), "https://x", TimeProvider.System);
        Assert.Equal(HttpStatusCode.NotFound, await Open(attacker.Create(sent.Id, DateTimeOffset.UtcNow.AddDays(30))));

        // Properly signed, but for a draft or an invoice that doesn't exist
        Assert.Equal(HttpStatusCode.NotFound, await Open(links.Create(draft.Id, DateTimeOffset.UtcNow.AddDays(30))));
        Assert.Equal(HttpStatusCode.NotFound, await Open(links.Create(Guid.NewGuid(), DateTimeOffset.UtcNow.AddDays(30))));
    }
}