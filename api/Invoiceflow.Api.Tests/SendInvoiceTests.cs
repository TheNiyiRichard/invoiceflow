using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.RegularExpressions;
using Invoiceflow.Api.Dtos;

namespace Invoiceflow.Api.Tests;

[Collection("api")]
public class SendInvoiceTests(ApiFactory factory)
{
    // Each test gets its own client address, so it only sees its own emails
    async Task<(HttpClient A, InvoiceDto Draft, string Email)> DraftFor(string clientName = "Test client", string domain = "example.com")
    {
        var a = await factory.SignedInUser("a");
        var email = $"{Guid.NewGuid():N}@{domain}";
        var client = await a.AddClient(clientName, email);
        return (a, await a.AddInvoice(client.Id), email);
    }

    [Fact]
    public async Task Sending_emails_a_working_link_and_locks_the_invoice()
    {
        var (a, draft, email) = await DraftFor();
        await factory.SetTaxRate(draft.Id, 0.13m); // as if the draft was started under a different rate

        var res = await a.PostAsync($"/api/invoices/{draft.Id}/send", null);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var sent = (await res.Content.ReadFromJsonAsync<InvoiceDto>())!;
        Assert.Equal("Sent", sent.Status);
        Assert.Equal(0.05m, sent.TaxRate); // locked at today's rate when sent
        Assert.Equal(315.00m, sent.Total);

        var mail = Assert.Single(factory.Mailbox.To(email));
        Assert.Contains(sent.Number, mail.Subject);
        var pdf = Assert.Single(mail.Attachments);
        Assert.Equal($"{sent.Number}.pdf", pdf.FileName);
        Assert.Equal("%PDF", Encoding.ASCII.GetString(pdf.Content, 0, 4));

        // The link in the email opens the invoice for someone who isn't signed in
        var token = Regex.Match(mail.Text, "/pay/([A-Za-z0-9_-]{76})").Groups[1].Value;
        var page = await factory.NewBrowser().GetFromJsonAsync<PaymentPageDto>($"/api/pay/{token}");
        Assert.Equal(315.00m, page!.Total);

        // Sent means locked
        var edit = await a.PutAsJsonAsync($"/api/invoices/{draft.Id}", TestApi.InvoiceBody(draft.ClientId));
        Assert.Equal(HttpStatusCode.Conflict, edit.StatusCode);
    }

    [Fact]
    public async Task An_invoice_is_only_ever_sent_once()
    {
        var (a, draft, email) = await DraftFor();
        Assert.Equal(HttpStatusCode.OK, (await a.PostAsync($"/api/invoices/{draft.Id}/send", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await a.PostAsync($"/api/invoices/{draft.Id}/send", null)).StatusCode);
        Assert.Single(factory.Mailbox.To(email));
    }

    [Fact]
    public async Task Names_show_as_text_in_the_email_never_as_html()
    {
        var (a, draft, email) = await DraftFor("<a href=\"https://evil.test\">Pay here</a>");
        await a.PostAsync($"/api/invoices/{draft.Id}/send", null);

        var mail = Assert.Single(factory.Mailbox.To(email));
        Assert.DoesNotContain("<a href=\"https://evil.test", mail.Html);
        Assert.Contains("&lt;a href=", mail.Html);
    }

    [Fact]
    public async Task If_the_email_fails_it_stays_a_draft()
    {
        var (a, draft, email) = await DraftFor(domain: "fail.test");

        var res = await a.PostAsync($"/api/invoices/{draft.Id}/send", null);

        Assert.Equal(HttpStatusCode.BadGateway, res.StatusCode);
        Assert.Equal("Draft", await a.StatusOf(draft.Id));
        Assert.Empty(factory.Mailbox.To(email));
    }
}