using System.Net;
using System.Net.Http.Json;
using Invoiceflow.Api.Dtos;

namespace Invoiceflow.Api.Tests;

[Collection("api")]
public class InvoiceTests(ApiFactory factory)
{
    [Fact]
    public async Task Owner_can_create_read_and_edit_a_draft()
    {
        var a = await factory.SignedInUser("a");
        var client = await a.AddClient();
        var created = await a.AddInvoice(client.Id);
        Assert.Equal("Draft", created.Status);
        Assert.Single(created.LineItems);

        var put = await a.PutAsJsonAsync($"/api/invoices/{created.Id}", TestApi.InvoiceBody(client.Id, "Updated work"));
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);

        var after = await a.GetFromJsonAsync<InvoiceDto>($"/api/invoices/{created.Id}");
        Assert.Equal("Updated work", after!.LineItems.Single().Description);
    }

    // Mass assignment: extra fields in the request must be ignored
    [Fact]
    public async Task Request_cannot_set_owner_status_or_totals()
    {
        var a = await factory.SignedInUser("a");
        var client = await a.AddClient();

        var res = await a.PostAsJsonAsync("/api/invoices", new
        {
            clientId = client.Id, issueDate = "2026-11-03", dueDate = "2026-11-17",
            lineItems = new[] { new { description = "Design work", quantity = 1, unitPrice = 100 } },
            status = "Paid", subtotal = 1, total = 1, ownerId = "someone-else",
        });
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);

        var invoice = (await res.Content.ReadFromJsonAsync<InvoiceDto>())!;
        Assert.Equal("Draft", invoice.Status);
        Assert.NotEqual(1m, invoice.Total);

        // Still A's: if ownerId had been accepted, A would get 404 here
        var mine = await a.GetAsync($"/api/invoices/{invoice.Id}");
        Assert.Equal(HttpStatusCode.OK, mine.StatusCode);
    }

    // IDOR through a foreign key: B points a new invoice at A's client
    [Fact]
    public async Task Cannot_invoice_another_users_client()
    {
        var a = await factory.SignedInUser("a");
        var b = await factory.SignedInUser("b");
        var aClient = await a.AddClient("Client of A");

        var res = await b.PostAsJsonAsync("/api/invoices", TestApi.InvoiceBody(aClient.Id));
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Client_with_invoices_cannot_be_deleted()
    {
        var a = await factory.SignedInUser("a");
        var client = await a.AddClient();
        await a.AddInvoice(client.Id);

        var res = await a.DeleteAsync($"/api/clients/{client.Id}");
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);

        var still = await a.GetAsync($"/api/clients/{client.Id}");
        Assert.Equal(HttpStatusCode.OK, still.StatusCode);
    }

    [Fact]
    public async Task List_filters_by_status_and_caps_page_size()
    {
        var a = await factory.SignedInUser("a");
        var client = await a.AddClient();
        await a.AddInvoice(client.Id);

        var drafts = await a.GetFromJsonAsync<PagedResult<InvoiceSummaryDto>>("/api/invoices?status=Draft&pageSize=1000");
        Assert.Equal(50, drafts!.PageSize);
        Assert.NotEmpty(drafts.Items);
        Assert.All(drafts.Items, i => Assert.Equal("Draft", i.Status));

        var paid = await a.GetFromJsonAsync<PagedResult<InvoiceSummaryDto>>("/api/invoices?status=Paid");
        Assert.Empty(paid!.Items);
    }
}