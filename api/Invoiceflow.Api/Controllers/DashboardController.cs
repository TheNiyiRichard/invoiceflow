using Invoiceflow.Api.Auth;
using Invoiceflow.Api.Data;
using Invoiceflow.Api.Dtos;
using Invoiceflow.Api.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Invoiceflow.Api.Controllers;

// No [Authorize]: sign-in is required by default (Day 15), and EndpointAuthTests checks this route too
[ApiController]
[Route("api/dashboard")]
public class DashboardController(AppDbContext db, CurrentUser currentUser) : ControllerBase
{
    const int DaysToPayWindow = 90;

    string UserId => currentUser.Id!;

    // Every query starts here: only the signed-in user's invoices
    IQueryable<Invoice> MyInvoices => db.Invoices.AsNoTracking().Where(i => i.OwnerId == UserId);

    // GET /api/dashboard/stats?timeZone=America/Toronto
    [HttpGet("stats")]
    public async Task<ActionResult<DashboardStatsDto>> Stats(string? timeZone)
    {
        var zone = FindZone(timeZone);
        if (zone is null)
        {
            ModelState.AddModelError(nameof(timeZone), "Unknown time zone.");
            return ValidationProblem(ModelState);
        }

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
}