using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using Invoiceflow.Api.Dtos;
using Invoiceflow.Api.Models;

namespace Invoiceflow.Api.Tests;

[Collection("api")]
public class CashFlowTests(ApiFactory factory)
{
    static string Month(DateTime d) => d.ToString("yyyy-MM", CultureInfo.InvariantCulture);

    [Fact]
    public async Task Cash_flow_puts_each_invoice_in_the_right_month()
    {
        // Only this test uses "e", so the totals are exact (every invoice is 315.00)
        var e = await factory.SignedInUser("e");
        var client = await e.AddClient();

        var now = DateTime.UtcNow;
        var today = DateOnly.FromDateTime(now);
        var monthStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var threeMonthsAgo = monthStart.AddMonths(-3); // midnight UTC on the 1st

        // Paid: counted in the month the money arrived
        var paidNow = await e.AddInvoice(client.Id);
        await factory.MarkPaid(paidNow.Id, today, now);
        var paidEarlier = await e.AddInvoice(client.Id);
        await factory.MarkPaid(paidEarlier.Id, DateOnly.FromDateTime(threeMonthsAgo).AddDays(-20), threeMonthsAgo);
        var paidTooLongAgo = await e.AddInvoice(client.Id);
        await factory.MarkPaid(paidTooLongAgo.Id, DateOnly.FromDateTime(monthStart.AddMonths(-13)), monthStart.AddMonths(-12)); // just outside the 12 months

        // Outstanding: still unpaid, counted in the month it was issued
        var sentNow = await e.AddInvoice(client.Id, today);
        await factory.SetStatus(sentNow.Id, InvoiceStatus.Sent);
        var overdueEarlier = await e.AddInvoice(client.Id, DateOnly.FromDateTime(monthStart.AddMonths(-2)));
        await factory.SetStatus(overdueEarlier.Id, InvoiceStatus.Overdue);
        await e.AddInvoice(client.Id, today); // stays a draft: never counted

        var flow = (await e.GetFromJsonAsync<List<CashFlowMonthDto>>("/api/dashboard/cash-flow"))!;

        Assert.Equal(12, flow.Count);
        Assert.Equal(Month(monthStart.AddMonths(-11)), flow[0].Month); // oldest first
        Assert.Equal(Month(now), flow[^1].Month);                     // ends this month
        Assert.Equal(315.00m, flow[^1].Paid);
        Assert.Equal(315.00m, flow[^1].Outstanding); // the draft and the paid invoice don't count
        Assert.Equal(315.00m, flow.Single(m => m.Month == Month(threeMonthsAgo)).Paid);
        Assert.Equal(315.00m, flow.Single(m => m.Month == Month(monthStart.AddMonths(-2))).Outstanding);
        Assert.Equal(630.00m, flow.Sum(m => m.Paid)); // the payment 12 months ago is left out
        Assert.Equal(630.00m, flow.Sum(m => m.Outstanding));

        // In Toronto, midnight UTC on the 1st is still the evening before, so that payment lands a month earlier
        var toronto = (await e.GetFromJsonAsync<List<CashFlowMonthDto>>("/api/dashboard/cash-flow?timeZone=America/Toronto"))!;
        Assert.Equal(315.00m, toronto.Single(m => m.Month == Month(threeMonthsAgo.AddDays(-1))).Paid);
        Assert.Equal(0m, toronto.Single(m => m.Month == Month(threeMonthsAgo)).Paid);
    }

    [Fact]
    public async Task Unknown_time_zone_is_rejected()
    {
        var a = await factory.SignedInUser("a");
        var res = await a.GetAsync("/api/dashboard/cash-flow?timeZone=Mars/Olympus_Mons");
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }
}