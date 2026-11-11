using Invoiceflow.Api.Data;
using Invoiceflow.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Invoiceflow.Api.Jobs;

// The work itself. Separate from the timer, so tests can run it with a fixed "now".
public class OverdueInvoices(AppDbContext db)
{
    public async Task<int> MarkAsync(DateTimeOffset now, CancellationToken ct = default)
    {
        // A due date lasts until the end of that day, wherever the user is. UTC-12 is the last
        // place on Earth where a day ends, so once a date is over there, it's over for everyone.
        var todayInUtcMinus12 = DateOnly.FromDateTime(now.UtcDateTime.AddHours(-12));

        // The only query in the app that skips the per-user filter, on purpose: a background job
        // has no signed-in user, so with the filter on it would see nothing at all.
        // It can do exactly one thing: move Sent or Viewed invoices past their due date to Overdue.
        // One UPDATE statement, so an invoice paid at the same moment is never overwritten.
        return await db.Invoices.IgnoreQueryFilters()
            .Where(i => (i.Status == InvoiceStatus.Sent || i.Status == InvoiceStatus.Viewed) && i.DueDate < todayInUtcMinus12)
            .ExecuteUpdateAsync(s => s
                .SetProperty(i => i.Status, InvoiceStatus.Overdue)
                .SetProperty(i => i.UpdatedAt, now.UtcDateTime), ct);
    }
}