namespace Invoiceflow.Api.Dtos;

public record DashboardStatsDto(
    decimal Outstanding, int OutstandingCount, decimal Overdue,
    decimal PaidThisMonth,
    decimal Drafts, int DraftCount,
    int? AverageDaysToPay); // null until something has been paid in the last 90 days

// One bar on the chart. Month is "yyyy-MM" in the user's time zone; the browser formats it.
public record CashFlowMonthDto(string Month, decimal Paid, decimal Outstanding);