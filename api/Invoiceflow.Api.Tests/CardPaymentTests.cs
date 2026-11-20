using System.Net;
using System.Net.Http.Json;
using Invoiceflow.Api.Dtos;
using Invoiceflow.Api.Models;
using Invoiceflow.Api.Payments;
using Microsoft.Extensions.DependencyInjection;

namespace Invoiceflow.Api.Tests;

[Collection("api")]
public class CardPaymentTests(ApiFactory factory)
{
    async Task<(Guid Id, string Token)> InvoiceWithLink(InvoiceStatus status)
    {
        var a = await factory.SignedInUser("a");
        var client = await a.AddClient();
        var invoice = await a.AddInvoice(client.Id);
        await factory.SetStatus(invoice.Id, status);
        var token = factory.Services.GetRequiredService<PaymentLinks>().Create(invoice.Id, DateTimeOffset.UtcNow.AddDays(30));
        return (invoice.Id, token);
    }

    [Fact]
    public async Task Amount_comes_from_the_saved_invoice_not_the_browser()
    {
        var (id, token) = await InvoiceWithLink(InvoiceStatus.Sent);
        await factory.SetTaxRate(id, 0.13m); // total 339.00

        // Whatever the browser sends is ignored
        var res = await factory.NewBrowser().PostAsJsonAsync($"/api/pay/{token}/intent", new { amount = 1, currency = "usd" });

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.True(res.Headers.CacheControl?.NoStore);
        var dto = (await res.Content.ReadFromJsonAsync<CardPaymentDto>())!;
        Assert.Equal("pk_test_fake", dto.PublishableKey);

        var intent = Assert.Single(factory.Payments.For(id));
        Assert.Equal(dto.ClientSecret, intent.ClientSecret);
        Assert.Equal(33900, intent.Request.AmountCents);
        Assert.Equal("cad", intent.Request.Currency);
    }

    [Fact]
    public async Task A_second_tab_gets_the_same_payment()
    {
        var (id, token) = await InvoiceWithLink(InvoiceStatus.Viewed);

        var first = await (await factory.NewBrowser().PostAsync($"/api/pay/{token}/intent", null)).Content.ReadFromJsonAsync<CardPaymentDto>();
        var second = await (await factory.NewBrowser().PostAsync($"/api/pay/{token}/intent", null)).Content.ReadFromJsonAsync<CardPaymentDto>();

        Assert.Equal(first!.ClientSecret, second!.ClientSecret);
        Assert.Single(factory.Payments.For(id));
    }

    [Fact]
    public async Task Once_stripe_has_the_money_it_cannot_be_started_again()
    {
        var (id, token) = await InvoiceWithLink(InvoiceStatus.Overdue); // overdue invoices can still be paid
        Assert.Equal(HttpStatusCode.OK, (await factory.NewBrowser().PostAsync($"/api/pay/{token}/intent", null)).StatusCode);

        factory.Payments.For(id).Single().Status = "succeeded"; // as if the client paid at Stripe

        var res = await factory.NewBrowser().PostAsync($"/api/pay/{token}/intent", null);
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        Assert.Single(factory.Payments.For(id));
    }

    [Theory]
    [InlineData(InvoiceStatus.Paid, HttpStatusCode.Conflict)]
    [InlineData(InvoiceStatus.Draft, HttpStatusCode.NotFound)]
    public async Task Paid_and_draft_invoices_cannot_be_charged(InvoiceStatus status, HttpStatusCode expected)
    {
        var (id, token) = await InvoiceWithLink(status);

        var res = await factory.NewBrowser().PostAsync($"/api/pay/{token}/intent", null);

        Assert.Equal(expected, res.StatusCode);
        Assert.Empty(factory.Payments.For(id));
    }
}