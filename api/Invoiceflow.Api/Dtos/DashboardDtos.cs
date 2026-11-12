namespace Invoiceflow.Api.Dtos;

public record DashboardStatsDto(
    decimal Outstanding, int OutstandingCount, decimal Overdue,
    decimal PaidThisMonth,
    decimal Drafts, int DraftCount,
    int? AverageDaysToPay); // null until something has been paid in the last 90 days