using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Invoiceflow.Api.Dtos;
using Invoiceflow.Api.Models;

namespace Invoiceflow.Api.Tests;

[Collection("api")]
public class ReminderTests(ApiFactory factory)
{
    // An invoice out with its client. Its own client address, so each test only sees its own emails.
    async Task<(HttpClient A, Guid Id, string Email)> SentInvoice(string domain = "example.com")
    {
        var a = await factory.SignedInUser("a");
        var email = $"{Guid.NewGuid():N}@{domain}";
        var client = await a.AddClient("Test client", email);
        var invoice = await a.AddInvoice(client.Id);
        await factory.SetStatus(invoice.Id, InvoiceStatus.Sent);
        return (a, invoice.Id, email);
    }

    static Task<HttpResponseMessage> Remind(HttpClient browser, Guid id) => browser.PostAsync($"/api/invoices/{id}/remind", null);

    [Fact]
    public async Task A_reminder_emails_a_fresh_working_link()
    {
        var (a, id, email) = await SentInvoice();

        var res = await Remind(a, id);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.NotNull((await res.Content.ReadFromJsonAsync<InvoiceDto>())!.LastRemindedAt);

        var mail = Assert.Single(factory.Mailbox.To(email));
        Assert.StartsWith("Reminder", mail.Subject);
        Assert.Single(mail.Attachments);

        // The new link opens the invoice for someone who isn't signed in
        var token = Regex.Match(mail.Text, "/pay/([A-Za-z0-9_-]{76})").Groups[1].Value;
        var page = await factory.NewBrowser().GetFromJsonAsync<PaymentPageDto>($"/api/pay/{token}");
        Assert.Equal(315.00m, page!.Total);
    }

    [Fact]
    public async Task One_reminder_per_invoice_per_day()
    {
        var (a, id, email) = await SentInvoice();
        Assert.Equal(HttpStatusCode.OK, (await Remind(a, id)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Remind(a, id)).StatusCode);
        Assert.Single(factory.Mailbox.To(email));

        await factory.SetLastReminded(id, DateTime.UtcNow.AddHours(-25)); // as if it's the next day
        Assert.Equal(HttpStatusCode.OK, (await Remind(a, id)).StatusCode);
        Assert.Equal(2, factory.Mailbox.To(email).Count);
    }

    [Theory]
    [InlineData(InvoiceStatus.Draft)]
    [InlineData(InvoiceStatus.Paid)]
    public async Task Only_sent_unpaid_invoices_get_reminders(InvoiceStatus status)
    {
        var (a, id, email) = await SentInvoice();
        await factory.SetStatus(id, status);

        Assert.Equal(HttpStatusCode.Conflict, (await Remind(a, id)).StatusCode);
        Assert.Empty(factory.Mailbox.To(email));
    }

    [Fact]
    public async Task If_the_reminder_fails_it_can_be_sent_again()
    {
        var (a, id, _) = await SentInvoice("fail.test");
        Assert.Equal(HttpStatusCode.BadGateway, (await Remind(a, id)).StatusCode);

        // Nothing went out, so it doesn't count towards the daily limit
        var invoice = await a.GetFromJsonAsync<InvoiceDto>($"/api/invoices/{id}");
        Assert.Null(invoice!.LastRemindedAt);
        Assert.Equal(HttpStatusCode.BadGateway, (await Remind(a, id)).StatusCode); // tried again: 502, not 409
    }
}