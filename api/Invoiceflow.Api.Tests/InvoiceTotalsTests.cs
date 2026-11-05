using System.Net;
using System.Net.Http.Json;
using Invoiceflow.Api.Dtos;

namespace Invoiceflow.Api.Tests;

[Collection("api")]
public class InvoiceTotalsTests(ApiFactory factory)
{
    static object Body(Guid clientId, object lineItems) =>
        new { clientId, issueDate = "2026-11-04", dueDate = "2026-11-18", lineItems };

    [Fact]
    public async Task Totals_use_decimal_and_round_half_up()
    {
        var a = await factory.SignedInUser("a");
        var client = await a.AddClient();

        var res = await a.PostAsJsonAsync("/api/invoices", Body(client.Id, new[]
        {
            new { description = "Design work", quantity = 2m, unitPrice = 150m },  // 300.00
            new { description = "Domain", quantity = 3m, unitPrice = 0.10m },      // 0.30 (a double gives 0.30000000000000004)
            new { description = "Short call", quantity = 0.5m, unitPrice = 4.69m }, // 2.345 rounds up to 2.35
        }));
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var invoice = (await res.Content.ReadFromJsonAsync<InvoiceDto>())!;

        Assert.Equal(new[] { 300.00m, 0.30m, 2.35m }, invoice.LineItems.Select(li => li.Amount));
        Assert.Equal(302.65m, invoice.Subtotal);
        Assert.Equal(0.05m, invoice.TaxRate);
        Assert.Equal(15.13m, invoice.Tax); // 302.65 × 5% = 15.1325
        Assert.Equal(317.78m, invoice.Total);
    }

    [Fact]
    public async Task Editing_a_draft_recalculates_totals()
    {
        var a = await factory.SignedInUser("a");
        var client = await a.AddClient();
        var invoice = await a.AddInvoice(client.Id); // 2 × 150
        Assert.Equal(315.00m, invoice.Total);

        var put = await a.PutAsJsonAsync($"/api/invoices/{invoice.Id}",
            Body(client.Id, new[] { new { description = "Smaller job", quantity = 1m, unitPrice = 80m } }));
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        var after = (await put.Content.ReadFromJsonAsync<InvoiceDto>())!;

        Assert.Equal(80.00m, after.Subtotal);
        Assert.Equal(4.00m, after.Tax);
        Assert.Equal(84.00m, after.Total);
        Assert.Equal(invoice.Number, after.Number); // editing never changes the number
    }

    // Mass assignment: a tampered request can't choose its own number or what the client owes
    [Fact]
    public async Task Request_cannot_set_number_or_totals()
    {
        var a = await factory.SignedInUser("a");
        var client = await a.AddClient();

        var res = await a.PostAsJsonAsync("/api/invoices", new
        {
            clientId = client.Id, issueDate = "2026-11-04", dueDate = "2026-11-18",
            lineItems = new[] { new { description = "Design work", quantity = 1m, unitPrice = 100m } },
            number = 9999, taxRate = 0, subtotal = 1, tax = 0, total = 1,
        });
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var invoice = (await res.Content.ReadFromJsonAsync<InvoiceDto>())!;

        Assert.NotEqual("INV-9999", invoice.Number);
        Assert.Equal(105.00m, invoice.Total);
    }

    [Fact]
    public async Task Oversized_invoice_gets_400_not_500()
    {
        var a = await factory.SignedInUser("a");
        var client = await a.AddClient();

        var res = await a.PostAsJsonAsync("/api/invoices",
            Body(client.Id, new[] { new { description = "Too much", quantity = 9_999_999m, unitPrice = 1_000_000_000m } }));
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    // Numbers are per user, so INV-0003 tells a client nothing about anyone else's business
    [Fact]
    public async Task Numbers_count_up_per_user()
    {
        var c = await factory.SignedInUser("c"); // only this test uses "c"
        var client = await c.AddClient();

        var first = await c.AddInvoice(client.Id);
        var second = await c.AddInvoice(client.Id);
        Assert.Equal("INV-0001", first.Number);
        Assert.Equal("INV-0002", second.Number);
    }

    [Fact]
    public async Task Simultaneous_creates_never_share_a_number()
    {
        var a = await factory.SignedInUser("a");
        var client = await a.AddClient();

        var created = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => a.AddInvoice(client.Id)));
        Assert.Equal(4, created.Select(i => i.Number).Distinct().Count());
    }
}