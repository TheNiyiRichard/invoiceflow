using Invoiceflow.Api.Models;

namespace Invoiceflow.Api.Tests;

// TestApi invoices are due 2026-11-17. The job runs here with a fixed "now",
// so these tests give the same result whatever day they're run.
[Collection("api")]
public class OverdueJobTests(ApiFactory factory)
{
    [Theory]
    [InlineData(InvoiceStatus.Sent)]
    [InlineData(InvoiceStatus.Viewed)]
    public async Task Unpaid_invoice_goes_overdue_once_its_due_date_is_over_everywhere(InvoiceStatus status)
    {
        var a = await factory.SignedInUser("a");
        var client = await a.AddClient();
        var invoice = await a.AddInvoice(client.Id);
        await factory.SetStatus(invoice.Id, status);

        // 11:59 UTC on the 18th: it's still the 17th in UTC-12, so not overdue yet
        await factory.RunOverdueJob(new DateTimeOffset(2026, 11, 18, 11, 59, 0, TimeSpan.Zero));
        Assert.Equal(status.ToString(), await a.StatusOf(invoice.Id));

        // 12:00 UTC: the 17th is over everywhere
        await factory.RunOverdueJob(new DateTimeOffset(2026, 11, 18, 12, 0, 0, TimeSpan.Zero));
        Assert.Equal("Overdue", await a.StatusOf(invoice.Id));
    }

    [Fact]
    public async Task Drafts_and_paid_invoices_never_go_overdue()
    {
        var a = await factory.SignedInUser("a");
        var client = await a.AddClient();
        var draft = await a.AddInvoice(client.Id);
        var paid = await a.AddInvoice(client.Id);
        await factory.SetStatus(paid.Id, InvoiceStatus.Paid);

        await factory.RunOverdueJob(new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero));

        Assert.Equal("Draft", await a.StatusOf(draft.Id));
        Assert.Equal("Paid", await a.StatusOf(paid.Id));
    }
}