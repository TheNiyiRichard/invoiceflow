using System.Net;
using System.Net.Http.Json;
using Invoiceflow.Api.Dtos;
using Invoiceflow.Api.Models;
using Invoiceflow.Api.Payments;
using Microsoft.Extensions.DependencyInjection;

namespace Invoiceflow.Api.Tests;

[Collection("api")]
public class PayPageTests(ApiFactory factory)
{
    async Task<(HttpClient A, InvoiceDto Invoice, string Token)> InvoiceWithLink(InvoiceStatus status)
    {
        var a = await factory.SignedInUser("a");
        var client = await a.AddClient();
        var invoice = await a.AddInvoice(client.Id);
        await factory.SetStatus(invoice.Id, status);
        var token = factory.Services.GetRequiredService<PaymentLinks>().Create(invoice.Id, DateTimeOffset.UtcNow.AddDays(30));
        return (a, invoice, token);
    }

    [Fact]
    public async Task Page_shows_the_lines_and_the_saved_totals()
    {
        var (_, invoice, token) = await InvoiceWithLink(InvoiceStatus.Sent);
        await factory.SetTaxRate(invoice.Id, 0.13m);

        var page = (await factory.NewBrowser().GetFromJsonAsync<PaymentPageDto>($"/api/pay/{token}"))!;

        var line = Assert.Single(page.Lines);
        Assert.Equal("Design work", line.Description);
        Assert.Equal(2m, line.Quantity);
        Assert.Equal(150m, line.UnitPrice);
        Assert.Equal(300m, line.Amount);
        Assert.Equal(300m, page.Subtotal);
        Assert.Equal(0.13m, page.TaxRate);
        Assert.Equal(39m, page.Tax);
        Assert.Equal(339m, page.Total);
    }

    [Fact]
    public async Task Only_the_viewed_call_marks_it_and_only_from_sent()
    {
        var visitor = factory.NewBrowser();

        var (a, sent, token) = await InvoiceWithLink(InvoiceStatus.Sent);
        await visitor.GetAsync($"/api/pay/{token}");
        Assert.Equal("Sent", await a.StatusOf(sent.Id)); // opening (a GET) changes nothing

        var res = await visitor.PostAsync($"/api/pay/{token}/viewed", null);
        Assert.Equal(HttpStatusCode.NoContent, res.StatusCode);
        Assert.Equal("Viewed", await a.StatusOf(sent.Id));

        // Overdue is more important than Viewed, so it stays
        var (_, overdue, overdueToken) = await InvoiceWithLink(InvoiceStatus.Overdue);
        await visitor.PostAsync($"/api/pay/{overdueToken}/viewed", null);
        Assert.Equal("Overdue", await a.StatusOf(overdue.Id));
    }

    [Fact]
    public async Task The_owner_opening_their_own_link_does_not_count()
    {
        var (a, invoice, token) = await InvoiceWithLink(InvoiceStatus.Sent);
        await a.PostAsync($"/api/pay/{token}/viewed", null); // a is signed in
        Assert.Equal("Sent", await a.StatusOf(invoice.Id));
    }

    [Fact]
    public async Task A_forged_link_cannot_mark_anything()
    {
        var res = await factory.NewBrowser().PostAsync($"/api/pay/{new string('A', 76)}/viewed", null);
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }
}