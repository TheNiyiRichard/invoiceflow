using Invoiceflow.Api.Data;
using Invoiceflow.Api.Mail;
using Invoiceflow.Api.Models;
using Invoiceflow.Api.Payments;
using Invoiceflow.Api.Pdf;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Invoiceflow.Api.Controllers;

// Stripe calls this when a payment succeeds. Public on purpose ([AllowAnonymous] AND EndpointAuthTests' allowlist):
// Stripe has no cookie, so its signature is the permission, checked before anything is read.
// This is the only code that can mark an invoice Paid.
[ApiController]
[AllowAnonymous]
[Route("api/webhooks/stripe")]
public class StripeWebhookController(AppDbContext db, StripeWebhooks webhooks, IPaymentGateway payments, IMailer mailer, ILogger<StripeWebhookController> logger) : ControllerBase
{
    [HttpPost]
    [RequestSizeLimit(256 * 1024)] // Stripe events are a few KB
    public async Task<IActionResult> Receive(CancellationToken ct)
    {
        // The exact text Stripe signed. Read as-is: parsing it first could change it and break the signature.
        using var reader = new StreamReader(Request.Body);
        var json = await reader.ReadToEndAsync(ct);

        // Check 1: the signature. Stripe sent it, nothing changed, signed in the last 5 minutes.
        var stripeEvent = webhooks.Verify(json, Request.Headers["Stripe-Signature"].ToString());
        if (stripeEvent is null) return BadRequest();

        var paid = StripeWebhooks.Succeeded(stripeEvent);
        if (paid is null)
        {
            // A payment event we couldn't read (an API version the library doesn't understand, say) is logged, never skipped quietly
            if (stripeEvent.Type == "payment_intent.succeeded")
                logger.LogWarning("Stripe event {EventId} couldn't be read as a payment. Nothing marked paid.", stripeEvent.Id);
            return Ok(); // every other event type: nothing to do, but 200 so Stripe stops retrying
        }

        // Nobody is signed in, so the per-user filter would hide everything. The verified event names the payment.
        var invoice = await db.Invoices.IgnoreQueryFilters().AsNoTracking()
            .Where(i => i.StripePaymentIntentId == paid.IntentId)
            .Select(i => new { i.Id, i.Total, i.Status })
            .FirstOrDefaultAsync(ct);

        // Check 2: the event matches what was saved. Same payment, same invoice, the full amount, in CAD.
        var cents = invoice is null ? 0 : (long)(invoice.Total * 100);
        if (invoice is null || invoice.Id != paid.InvoiceId || paid.AmountCents != cents || paid.Currency != "cad")
        {
            logger.LogWarning("Stripe event {EventId} doesn't match a saved payment. Nothing marked paid.", paid.EventId);
            return Ok(); // retrying won't change the answer
        }

        // Stripe sometimes delivers the same event twice. Already paid: nothing to do, and no need to ask Stripe again.
        if (invoice.Status == InvoiceStatus.Paid)
        {
            logger.LogInformation("Stripe event {EventId}: invoice {InvoiceId} was already paid", paid.EventId, invoice.Id);
            return Ok();
        }

        // Check 3: ask Stripe's API directly. Someone holding the webhook secret could sign an event,
        // but they can't change what Stripe itself says about the payment.
        CardPayment confirmed;
        try
        {
            confirmed = await payments.GetAsync(paid.IntentId, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Couldn't confirm Stripe event {EventId} with Stripe", paid.EventId);
            return StatusCode(StatusCodes.Status503ServiceUnavailable); // not a 2xx, so Stripe sends it again later
        }
        if (confirmed.Status != "succeeded" || confirmed.AmountReceived != cents || confirmed.Currency != "cad")
        {
            logger.LogWarning("Stripe event {EventId} isn't confirmed by Stripe's API. Nothing marked paid.", paid.EventId);
            return Ok();
        }

        // One statement, and only from an unpaid status, so two deliveries at the same moment still mark it once
        var marked = await db.Invoices.IgnoreQueryFilters()
            .Where(i => i.Id == invoice.Id && i.StripePaymentIntentId == paid.IntentId
                && (i.Status == InvoiceStatus.Sent || i.Status == InvoiceStatus.Viewed || i.Status == InvoiceStatus.Overdue))
            .ExecuteUpdateAsync(s => s
                .SetProperty(i => i.Status, InvoiceStatus.Paid)
                .SetProperty(i => i.PaidAt, (DateTime?)paid.PaidAt) // when Stripe took the money, even if this is a late retry
                .SetProperty(i => i.UpdatedAt, DateTime.UtcNow), ct);

        // Only the delivery that actually marked it Paid sends a receipt, so a repeat never sends a second one
        if (marked == 1) await SendReceipt(invoice.Id);

        logger.LogInformation("Stripe event {EventId}: invoice {InvoiceId} {Result}", paid.EventId, invoice.Id, marked == 1 ? "marked paid" : "was already paid");
        return Ok();
    }

    // The client's receipt, with the PDF stamped PAID. If the email fails, the payment still stands:
    // it's logged by id and Stripe still gets its 200 (retrying wouldn't resend it anyway).
    async Task SendReceipt(Guid invoiceId)
    {
        try
        {
            var paidInvoice = await db.Invoices.IgnoreQueryFilters().AsNoTracking()
                .Include(i => i.Client).Include(i => i.LineItems)
                .FirstAsync(i => i.Id == invoiceId);
            var sender = await db.SenderAsync(paidInvoice.OwnerId);
            await mailer.SendAsync(InvoiceEmail.Receipt(paidInvoice, sender, InvoicePdf.Render(paidInvoice, sender)));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Receipt for invoice {InvoiceId} wasn't sent", invoiceId);
        }
    }
}