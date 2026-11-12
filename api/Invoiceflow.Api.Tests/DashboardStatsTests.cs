using System.Net;
using System.Net.Http.Json;
using Invoiceflow.Api.Dtos;
using Invoiceflow.Api.Models;

namespace Invoiceflow.Api.Tests;

[Collection("api")]
public class DashboardStatsTests(ApiFactory factory)
{
    [Fact]
    public async Task Stats_add_up_only_this_users_invoices()
    {
        // Only this test uses "d", so other tests' invoices (users a, b, c) must not show up here
        var d = await factory.SignedInUser("d");
        var client = await d.AddClient();
        var sent = await d.AddInvoice(client.Id); // every invoice: 2 × 150 + 5% GST = 315.00
        var overdue = await d.AddInvoice(client.Id);
        var paidThisMonth = await d.AddInvoice(client.Id);
        var paidLastMonth = await d.AddInvoice(client.Id);
        await d.AddInvoice(client.Id); // stays a draft
        await factory.SetStatus(sent.Id, InvoiceStatus.Sent);
        await factory.SetStatus(overdue.Id, InvoiceStatus.Overdue);

        // Dates relative to today, so the test gives the same result whenever it runs
        var now = DateTime.UtcNow;
        var monthStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var lastSecondOfLastMonth = monthStart.AddSeconds(-1);
        await factory.MarkPaid(paidThisMonth.Id, DateOnly.FromDateTime(monthStart).AddDays(-10), monthStart);                     // 10 days to pay
        await factory.MarkPaid(paidLastMonth.Id, DateOnly.FromDateTime(lastSecondOfLastMonth).AddDays(-20), lastSecondOfLastMonth); // 20 days to pay

        var stats = (await d.GetFromJsonAsync<DashboardStatsDto>("/api/dashboard/stats"))!;

        Assert.Equal(630.00m, stats.Outstanding); // sent + overdue
        Assert.Equal(2, stats.OutstandingCount);
        Assert.Equal(315.00m, stats.Overdue);
        Assert.Equal(315.00m, stats.PaidThisMonth); // last month's payment doesn't count
        Assert.Equal(315.00m, stats.Drafts);
        Assert.Equal(1, stats.DraftCount);
        Assert.Equal(15, stats.AverageDaysToPay); // (10 + 20) / 2
    }

    [Fact]
    public async Task Unknown_time_zone_is_rejected()
    {
        var a = await factory.SignedInUser("a");

        var bad = await a.GetAsync("/api/dashboard/stats?timeZone=Mars/Olympus_Mons");
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);

        var good = await a.GetAsync("/api/dashboard/stats?timeZone=America/Toronto");
        Assert.Equal(HttpStatusCode.OK, good.StatusCode);
    }
}