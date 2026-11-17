using Invoiceflow.Api.Data;
using Invoiceflow.Api.Dtos;
using Invoiceflow.Api.Models;
using Invoiceflow.Api.Payments;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace Invoiceflow.Api.Controllers;

// The one place a signed-out visitor can see an invoice: only with a valid signed link.
// Public on purpose: [AllowAnonymous] here AND an entry in EndpointAuthTests' allowlist. Rate-limited per IP.
[ApiController]
[AllowAnonymous]
[EnableRateLimiting("pay")]
[Route("api/pay")]
public class PayController(AppDbContext db, PaymentLinks links) : ControllerBase
{
    // GET /api/pay/{token}
    [HttpGet("{token}")]
    public async Task<ActionResult<PaymentPageDto>> Get(string token)
    {
        // Financial details: never kept in a browser or shared cache
        Response.Headers.CacheControl = "no-store";

        var (status, invoiceId) = links.Check(token);
        if (status == LinkStatus.Expired)
            return Problem(statusCode: StatusCodes.Status410Gone, title: "This payment link has expired. Ask the sender for a new one.");
        if (status != LinkStatus.Valid) return NotValid();

        // Nobody is signed in, so the per-user filter would hide everything. Here the signature is the
        // permission, and it names exactly one invoice. Drafts never show, even with a signed link.
        var invoice = await db.Invoices.IgnoreQueryFilters().AsNoTracking()
            .Where(i => i.Id == invoiceId && i.Status != InvoiceStatus.Draft)
            .Select(i => new { i.Number, i.Status, i.DueDate, i.Total, i.OwnerId })
            .FirstOrDefaultAsync();
        if (invoice is null) return NotValid();

        var owner = await db.Users.AsNoTracking()
            .Where(u => u.Id == invoice.OwnerId)
            .Select(u => new { u.FullName, u.BusinessName })
            .FirstAsync();
        var from = string.IsNullOrWhiteSpace(owner.BusinessName) ? owner.FullName : owner.BusinessName;

        return new PaymentPageDto(InvoiceMath.FormatNumber(invoice.Number), from, invoice.Status.ToString(), invoice.DueDate, invoice.Total);
    }

    // Same answer for forged, tampered, unknown and draft, so a guesser learns nothing
    ObjectResult NotValid() => Problem(statusCode: StatusCodes.Status404NotFound, title: "This payment link isn't valid.");
}