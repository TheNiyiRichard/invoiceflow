using Invoiceflow.Api.Auth;
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
// Public on purpose: [AllowAnonymous] here AND entries in EndpointAuthTests' allowlist. Rate-limited per IP.
[ApiController]
[AllowAnonymous]
[EnableRateLimiting("pay")]
[Route("api/pay")]
public class PayController(AppDbContext db, PaymentLinks links, CurrentUser currentUser, IPaymentGateway payments, ILogger<PayController> logger) : ControllerBase
{
    // GET /api/pay/{token}: what the payment page shows. Reads only, never changes anything.
    [HttpGet("{token}")]
    public async Task<ActionResult<PaymentPageDto>> Get(string token)
    {
        // Financial details: never kept in a browser or shared cache
        Response.Headers.CacheControl = "no-store";

        var (problem, invoiceId) = Open(token);
        if (problem is not null) return problem;

        // Nobody is signed in, so the per-user filter would hide everything. Here the signature is the
        // permission, and it names exactly one invoice. Drafts never show, even with a signed link.
        var invoice = await db.Invoices.IgnoreQueryFilters().AsNoTracking()
            .Where(i => i.Id == invoiceId && i.Status != InvoiceStatus.Draft)
            .Select(i => new
            {
                i.Number, i.Status, i.IssueDate, i.DueDate, i.Subtotal, i.TaxRate, i.Tax, i.Total, i.OwnerId,
                Lines = i.LineItems.OrderBy(li => li.Position)
                    .Select(li => new { li.Position, li.Description, li.Quantity, li.UnitPrice }).ToList(),
            })
            .FirstOrDefaultAsync();
        if (invoice is null) return NotValid();

        var owner = await db.Users.AsNoTracking()
            .Where(u => u.Id == invoice.OwnerId)
            .Select(u => new { u.FullName, u.BusinessName })
            .FirstAsync();
        var from = string.IsNullOrWhiteSpace(owner.BusinessName) ? owner.FullName : owner.BusinessName;

        // The saved totals and rate, exactly as sent. Never recalculated here.
        return new PaymentPageDto(
            InvoiceMath.FormatNumber(invoice.Number), from, invoice.Status.ToString(), invoice.IssueDate, invoice.DueDate,
            invoice.Lines.Select(li => new PaymentLineDto(li.Position, li.Description, li.Quantity, li.UnitPrice,
                InvoiceMath.LineAmount(li.Quantity, li.UnitPrice))).ToList(),
            invoice.Subtotal, invoice.TaxRate, invoice.Tax, invoice.Total);
    }

    // POST /api/pay/{token}/viewed: the page calls this once the invoice is on screen.
    // Separate from the GET on purpose: email scanners pre-open links, and they must never count as the client.
    [HttpPost("{token}/viewed")]
    public async Task<IActionResult> Viewed(string token)
    {
        var (problem, invoiceId) = Open(token);
        if (problem is not null) return problem;

        // The owner checking their own link doesn't count
        var me = currentUser.Id;

        // Only Sent becomes Viewed (Overdue and Paid stay as they are). One statement, so repeats change nothing.
        await db.Invoices.IgnoreQueryFilters()
            .Where(i => i.Id == invoiceId && i.Status == InvoiceStatus.Sent && (me == null || i.OwnerId != me))
            .ExecuteUpdateAsync(s => s
                .SetProperty(i => i.Status, InvoiceStatus.Viewed)
                .SetProperty(i => i.UpdatedAt, DateTime.UtcNow));
        return NoContent();
    }

        // Stripe's minimum card charge in CAD is $0.50
    const long MinimumCents = 50;

    // POST /api/pay/{token}/intent: starts (or picks up) the card payment for this invoice.
    // There is no request body: the amount always comes from the saved invoice, never from the browser.
    [HttpPost("{token}/intent")]
    public async Task<ActionResult<CardPaymentDto>> Intent(string token, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";

        var (problem, invoiceId) = Open(token);
        if (problem is not null) return problem;

        var invoice = await db.Invoices.IgnoreQueryFilters()
            .FirstOrDefaultAsync(i => i.Id == invoiceId && i.Status != InvoiceStatus.Draft, ct);
        if (invoice is null) return NotValid();
        if (invoice.Status == InvoiceStatus.Paid) return AlreadyPaid();

        var cents = (long)(invoice.Total * 100); // decimal(18,2), so this is exact
        if (cents < MinimumCents)
            return Problem(statusCode: StatusCodes.Status422UnprocessableEntity, title: "This invoice is too small to pay by card.");

        try
        {
            // One payment per invoice: a refresh or a second tab picks up the same one, so it can't be paid twice
            var payment = invoice.StripePaymentIntentId is { } existing ? await payments.GetAsync(existing, ct) : null;
            if (payment is { Status: "succeeded" or "processing" }) return AlreadyPaid();

            if (payment is null or { Status: "canceled" })
            {
                // Same key, same payment: if two requests race here, Stripe hands both the same one
                var key = payment is null ? $"invoice-{invoice.Id}-first" : $"invoice-{invoice.Id}-after-{payment.Id}";
                payment = await payments.CreateAsync(
                    new CardPaymentRequest(invoice.Id, cents, "cad", $"Invoice {InvoiceMath.FormatNumber(invoice.Number)}"), key, ct);
                invoice.StripePaymentIntentId = payment.Id;
                await db.SaveChangesAsync(ct);
            }

            return new CardPaymentDto(payment.ClientSecret, payments.PublishableKey);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Logged by invoice id only: no token, no amounts
            logger.LogError(ex, "Starting card payment for invoice {InvoiceId} failed", invoice.Id);
            return Problem(statusCode: StatusCodes.Status502BadGateway, title: "Card payment isn't available right now.");
        }
    }

    ObjectResult AlreadyPaid() => Problem(statusCode: StatusCodes.Status409Conflict, title: "This invoice has already been paid.");

    // Signature first. "Expired" is only said once the link is known to be genuine.
    (ObjectResult? Problem, Guid InvoiceId) Open(string token)
    {
        var (status, invoiceId) = links.Check(token);
        return status switch
        {
            LinkStatus.Valid => (null, invoiceId),
            LinkStatus.Expired => (Problem(statusCode: StatusCodes.Status410Gone, title: "This payment link has expired. Ask the sender for a new one."), Guid.Empty),
            _ => (NotValid(), Guid.Empty),
        };
    }

    // Same answer for forged, tampered, unknown and draft, so a guesser learns nothing
    ObjectResult NotValid() => Problem(statusCode: StatusCodes.Status404NotFound, title: "This payment link isn't valid.");
}