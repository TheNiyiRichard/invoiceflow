using Invoiceflow.Api.Data;
using Invoiceflow.Api.Pdf;
using Microsoft.EntityFrameworkCore;

namespace Invoiceflow.Api.Mail;

public static class Senders
{
    // Who an invoice is from: the business name if there is one, otherwise the person's name
    public static async Task<InvoicePdf.Sender> SenderAsync(this AppDbContext db, string userId)
    {
        var me = await db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new { u.FullName, u.BusinessName, u.Email })
            .FirstAsync();
        return new InvoicePdf.Sender(string.IsNullOrWhiteSpace(me.BusinessName) ? me.FullName : me.BusinessName, me.Email ?? "");
    }
}