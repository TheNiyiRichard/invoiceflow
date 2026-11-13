using System.Globalization;
using Invoiceflow.Api.Auth;
using Invoiceflow.Api.Data;
using Invoiceflow.Api.Dtos;
using Invoiceflow.Api.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Invoiceflow.Api.Controllers;

// No [Authorize]: sign-in is required by default (Day 15), and EndpointAuthTests checks these routes too
[ApiController]
[Route("api/dashboard")]
public class DashboardController(AppDbContext db, CurrentUser currentUser) : ControllerBase
{
    const int DaysToPayWindow = 90;
    const int CashFlowMonths = 12; // fixed on purpose: nobody can ask for a 50-year report

    string UserId => currentUser.Id!;

    // Every query starts here: only the signed-in user's invoices
    IQueryable<Invoice> MyInvoices => db.Invoices.AsNoTracking().Where(i => i.OwnerId == UserId);

    // GET /api/dashboard/stats?timeZone=America/Toronto
    [HttpGet("stats")]
    public async Task<ActionResult<DashboardStatsDto>> Stats(string? timeZone)
    {
        var zone = FindZone(timeZone);
        if (zone is null) return UnknownZone();

        // One query: how many invoices, and their total, for each status
        var byStatus = await MyInvoices
            .GroupBy(i => i.Status)
            .Select(g => new { Status = g.Key, Count = g.Count(), Total = g.Sum(i => i.Total) })
            .ToListAsync();

        var open = byStatus.Where(s => s.Status is InvoiceStatus.Sent or InvoiceStatus.Viewed or InvoiceStatus.Overdue).ToList();
        var overdue = byStatus.FirstOrDefault(s => s.Status == InvoiceStatus.Overdue);
        var drafts = byStatus.FirstOrDefault(s => s.Status == InvoiceStatus.Draft);

        // "This month" is the user's month, not the server's
        var nowUtc = DateTime.UtcNow;
        var local = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, zone);
        var firstOfMonth = new DateTime(local.Year, local.Month, 1);
        var monthStart = ToUtc(firstOfMonth, zone);
        var nextMonthStart = ToUtc(firstOfMonth.AddMonths(1), zone);

        var paidThisMonth = await MyInvoices
            .Where(i => i.Status == InvoiceStatus.Paid && i.PaidAt >= monthStart && i.PaidAt < nextMonthStart)
            .SumAsync(i => i.Total);

        // Days from the issue date to the day it was paid (in the user's time zone)
        var since = nowUtc.AddDays(-DaysToPayWindow);
        var paid = await MyInvoices
            .Where(i => i.Status == InvoiceStatus.Paid && i.PaidAt >= since)
            .Select(i => new { i.IssueDate, i.PaidAt })
            .ToListAsync();
        int? averageDaysToPay = paid.Count == 0 ? null : (int)Math.Round(
            paid.Average(p => Math.Max(0, LocalDate(p.PaidAt!.Value, zone).DayNumber - p.IssueDate.DayNumber)),
            MidpointRounding.AwayFromZero);

        return new DashboardStatsDto(
            open.Sum(s => s.Total), open.Sum(s => s.Count), overdue?.Total ?? 0,
            paidThisMonth,
            drafts?.Total ?? 0, drafts?.Count ?? 0,
            averageDaysToPay);
    }

    // GET /api/dashboard/cash-flow?timeZone=America/Toronto
    // 12 months, oldest first, ending with the user's current month
    [HttpGet("cash-flow")]
    public async Task<ActionResult<List<CashFlowMonthDto>>> CashFlow(string? timeZone)
    {
        var zone = FindZone(timeZone);
        if (zone is null) return UnknownZone();

        var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, zone);
        var thisMonth = new DateOnly(local.Year, local.Month, 1);
        var firstMonth = thisMonth.AddMonths(-(CashFlowMonths - 1));
        var nextMonth = thisMonth.AddMonths(1);
        var fromUtc = ToUtc(firstMonth.ToDateTime(TimeOnly.MinValue), zone);
        var toUtc = ToUtc(nextMonth.ToDateTime(TimeOnly.MinValue), zone);

        // Paid: money in, by the month it was paid
        var paid = await MyInvoices
            .Where(i => i.Status == InvoiceStatus.Paid && i.PaidAt >= fromUtc && i.PaidAt < toUtc)
            .Select(i => new { i.PaidAt, i.Total })
            .ToListAsync();

        // Outstanding: sent but not paid yet, by the month it was issued (drafts never count)
        var open = await MyInvoices
            .Where(i => (i.Status == InvoiceStatus.Sent || i.Status == InvoiceStatus.Viewed || i.Status == InvoiceStatus.Overdue)
                && i.IssueDate >= firstMonth && i.IssueDate < nextMonth)
            .Select(i => new { i.IssueDate, i.Total })
            .ToListAsync();

        var paidByMonth = paid
            .GroupBy(p => MonthOf(LocalDate(p.PaidAt!.Value, zone)))
            .ToDictionary(g => g.Key, g => g.Sum(p => p.Total));
        var openByMonth = open
            .GroupBy(o => MonthOf(o.IssueDate))
            .ToDictionary(g => g.Key, g => g.Sum(o => o.Total));

        // Every month gets a row, even an empty one, so the chart never has gaps
        return Enumerable.Range(0, CashFlowMonths)
            .Select(n => firstMonth.AddMonths(n))
            .Select(m => new CashFlowMonthDto(
                m.ToString("yyyy-MM", CultureInfo.InvariantCulture),
                paidByMonth.GetValueOrDefault(m),
                openByMonth.GetValueOrDefault(m)))
            .ToList();
    }

    ActionResult UnknownZone()
    {
        ModelState.AddModelError("timeZone", "Unknown time zone.");
        return ValidationProblem(ModelState);
    }

    // The browser sends its time zone. Only zones this server knows are accepted; anything else gets a 400.
    static TimeZoneInfo? FindZone(string? id) =>
        id is null ? TimeZoneInfo.Utc
        : id.Length <= 64 && TimeZoneInfo.TryFindSystemTimeZoneById(id, out var zone) ? zone
        : null;

    // Midnight in the user's zone, as UTC. A few zones skip the hour after midnight when clocks change, so use 1am there.
    static DateTime ToUtc(DateTime local, TimeZoneInfo zone) =>
        TimeZoneInfo.ConvertTimeToUtc(zone.IsInvalidTime(local) ? local.AddHours(1) : local, zone);

    static DateOnly LocalDate(DateTime utc, TimeZoneInfo zone) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), zone));

    static DateOnly MonthOf(DateOnly d) => new(d.Year, d.Month, 1);
}