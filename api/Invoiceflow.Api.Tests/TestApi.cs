using System.Net;
using System.Net.Http.Json;
using Invoiceflow.Api.Dtos;

namespace Invoiceflow.Api.Tests;

// Shortcuts the tests share
static class TestApi
{
    public static object InvoiceBody(Guid clientId, string description = "Design work", DateOnly? issued = null) => new
    {
        clientId,
        issueDate = issued ?? new DateOnly(2026, 11, 3),
        dueDate = (issued ?? new DateOnly(2026, 11, 3)).AddDays(14),
        lineItems = new[] { new { description, quantity = 2, unitPrice = 150 } },
    };

    public static async Task<ClientDto> AddClient(this HttpClient browser, string name = "Test client")
    {
        var res = await browser.PostAsJsonAsync("/api/clients", new { name, email = "client@example.com" });
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        return (await res.Content.ReadFromJsonAsync<ClientDto>())!;
    }

    public static async Task<InvoiceDto> AddInvoice(this HttpClient browser, Guid clientId, DateOnly? issued = null)
    {
        var res = await browser.PostAsJsonAsync("/api/invoices", InvoiceBody(clientId, issued: issued));
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        return (await res.Content.ReadFromJsonAsync<InvoiceDto>())!;
    }
    
    public static async Task<string> StatusOf(this HttpClient browser, Guid invoiceId) =>
        (await browser.GetFromJsonAsync<InvoiceDto>($"/api/invoices/{invoiceId}"))!.Status;
}