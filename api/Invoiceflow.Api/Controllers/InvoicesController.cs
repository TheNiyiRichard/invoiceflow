using Invoiceflow.Api.Auth;
using Invoiceflow.Api.Data;
using Invoiceflow.Api.Dtos;
using Invoiceflow.Api.Mail;
using Invoiceflow.Api.Models;
using Invoiceflow.Api.Payments;
using Invoiceflow.Api.Pdf;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Invoiceflow.Api.Controllers;

// No [Authorize] here on purpose: sign-in is required by default (Day 15),
// and EndpointAuthTests proves these routes refuse signed-out requests.
[ApiController]
[Route("api/invoices")]
public class InvoicesController(AppDbContext db, CurrentUser currentUser, PaymentLinks links, IMailer mailer, ILogger<InvoicesController> logger) : ControllerBase
{
    const int MaxPageSize = 50;
    const int MaxNumberAttempts = 5;

    string UserId => currentUser.Id!;

    // Every query starts here: only the signed-in user's invoices
    IQueryable<Invoice> MyInvoices => db.Invoices.Where(i => i.OwnerId == UserId);

    [HttpGet]
    public async Task<PagedResult<InvoiceSummaryDto>> GetAll(InvoiceStatus? status, int page = 1, int pageSize = 20)
    {
        // Capped, so nobody can ask for a million rows in one request
        page = Math.Clamp(page, 1, 100_000);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);

        var query = MyInvoices.AsNoTracking();
        if (status is not null) query = query.Where(i => i.Status == status.Value);

        var total = await query.CountAsync();
        var items = await query
            .OrderByDescending(i => i.IssueDate).ThenByDescending(i => i.CreatedAt)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(i => new InvoiceSummaryDto(i.Id, InvoiceMath.FormatNumber(i.Number), i.ClientId, i.Client.Name, i.Status.ToString(), i.IssueDate, i.DueDate, i.Total))
            .ToListAsync();

        return new PagedResult<InvoiceSummaryDto>(items, page, pageSize, total);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<InvoiceDto>> GetOne(Guid id)
    {
        var invoice = await MyInvoices.AsNoTracking()
            .Include(i => i.Client).Include(i => i.LineItems)
            .FirstOrDefaultAsync(i => i.Id == id);
        return invoice is null ? NotFound() : ToDto(invoice);
    }

    // GET /api/invoices/{id}/pdf: built on the server from the saved invoice, never from what the browser shows
    [HttpGet("{id:guid}/pdf")]
    public async Task<IActionResult> Pdf(Guid id)
    {
        var invoice = await MyInvoices.AsNoTracking()
            .Include(i => i.Client).Include(i => i.LineItems)
            .FirstOrDefaultAsync(i => i.Id == id);
        if (invoice is null) return NotFound(); // someone else's invoice looks exactly like a missing one

        // A financial document: never kept in a browser or shared cache
        Response.Headers.CacheControl = "no-store";
        // The file name comes from the server's own number, never from typed text like a client's name
        return File(InvoicePdf.Render(invoice, await SenderAsync()), "application/pdf", $"{InvoiceMath.FormatNumber(invoice.Number)}.pdf");
    }

    // POST /api/invoices/{id}/payment-link: a signed link the client can pay with, valid for 30 days.
    // Nothing is saved. Each call signs a fresh link, and older ones keep working until they expire.
    [HttpPost("{id:guid}/payment-link")]
    public async Task<ActionResult<PaymentLinkDto>> PaymentLink(Guid id)
    {
        var invoice = await MyInvoices.AsNoTracking().FirstOrDefaultAsync(i => i.Id == id);
        if (invoice is null) return NotFound();

        // Only invoices that are out with the client and still unpaid
        if (invoice.Status is InvoiceStatus.Draft or InvoiceStatus.Paid)
            return Problem(statusCode: StatusCodes.Status409Conflict,
                title: invoice.Status == InvoiceStatus.Draft ? "Send the invoice before sharing a payment link." : "This invoice is already paid.");

        var (url, expiresAt) = links.CreateLink(invoice.Id);
        return new PaymentLinkDto(url, expiresAt);
    }

    // POST /api/invoices/{id}/send: emails the client the PDF and a signed payment link. From then on it's locked.
    // Limited per user, so nobody can use Invoiceflow to spam people.
    [HttpPost("{id:guid}/send")]
    [EnableRateLimiting("send")]
    public async Task<ActionResult<InvoiceDto>> Send(Guid id)
    {
        // Claimed in one statement: only a draft can become Sent. If two clicks arrive together,
        // the database lets exactly one through, so the client never gets the email twice.
        var claimed = await MyInvoices
            .Where(i => i.Id == id && i.Status == InvoiceStatus.Draft)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.Status, InvoiceStatus.Sent));
        if (claimed == 0)
            return await MyInvoices.AnyAsync(i => i.Id == id)
                ? Problem(statusCode: StatusCodes.Status409Conflict, title: "This invoice has already been sent.")
                : NotFound();

        var invoice = await MyInvoices.Include(i => i.Client).Include(i => i.LineItems).FirstAsync(i => i.Id == id);

        // The GST rate is locked now, when it's sent, not when the draft was started
        invoice.TaxRate = InvoiceMath.GstRate;
        InvoiceMath.Recalculate(invoice);
        invoice.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        var sender = await SenderAsync();
        var (url, expiresAt) = links.CreateLink(invoice.Id);
        try
        {
            await mailer.SendAsync(InvoiceEmail.Build(invoice, sender, url, expiresAt, InvoicePdf.Render(invoice, sender)));
        }
        catch (Exception ex)
        {
            // The email didn't go out, so it goes back to being a draft that can be sent again. Logged by id.
            logger.LogError(ex, "Sending invoice {InvoiceId} failed", invoice.Id);
            invoice.Status = InvoiceStatus.Draft;
            await db.SaveChangesAsync();
            return Problem(statusCode: StatusCodes.Status502BadGateway, title: "Couldn't send the email. The invoice is still a draft.");
        }

        return ToDto(invoice);
    }

    static readonly TimeSpan ReminderCooldown = TimeSpan.FromHours(24);

    // POST /api/invoices/{id}/remind: emails the client again, with the PDF and a freshly signed 30-day link.
    // One per invoice per day, on top of the per-user send limit, so nobody can flood a client's inbox.
    [HttpPost("{id:guid}/remind")]
    [EnableRateLimiting("send")]
    public async Task<ActionResult<InvoiceDto>> Remind(Guid id)
    {
        var invoice = await MyInvoices.AsNoTracking()
            .Include(i => i.Client).Include(i => i.LineItems)
            .FirstOrDefaultAsync(i => i.Id == id);
        if (invoice is null) return NotFound();
        if (invoice.Status is not (InvoiceStatus.Sent or InvoiceStatus.Viewed or InvoiceStatus.Overdue))
            return Problem(statusCode: StatusCodes.Status409Conflict, title: "Only sent invoices that aren't paid yet can get a reminder.");

        // Claimed in one statement, like sending: two quick clicks can't both get through
        var now = DateTime.UtcNow;
        var cutoff = now - ReminderCooldown;
        var previous = invoice.LastRemindedAt;
        var claimed = await MyInvoices
            .Where(i => i.Id == id
                && (i.Status == InvoiceStatus.Sent || i.Status == InvoiceStatus.Viewed || i.Status == InvoiceStatus.Overdue)
                && (i.LastRemindedAt == null || i.LastRemindedAt <= cutoff))
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.LastRemindedAt, (DateTime?)now));
        if (claimed == 0)
            return Problem(statusCode: StatusCodes.Status409Conflict, title: "A reminder already went out in the last 24 hours. You can send another tomorrow.");

        var sender = await SenderAsync();
        var (url, expiresAt) = links.CreateLink(invoice.Id);
        try
        {
            await mailer.SendAsync(InvoiceEmail.Reminder(invoice, sender, url, expiresAt, InvoicePdf.Render(invoice, sender)));
        }
        catch (Exception ex)
        {
            // Nothing went out, so it doesn't count: the button works again straight away
            logger.LogError(ex, "Reminder for invoice {InvoiceId} failed", invoice.Id);
            await MyInvoices.Where(i => i.Id == id && i.LastRemindedAt == now)
                .ExecuteUpdateAsync(s => s.SetProperty(i => i.LastRemindedAt, previous));
            return Problem(statusCode: StatusCodes.Status502BadGateway, title: "Couldn't send the reminder. Nothing went out, so you can try again.");
        }

        invoice.LastRemindedAt = now;
        return ToDto(invoice);
    }

    [HttpPost]
    public async Task<ActionResult<InvoiceDto>> Create(InvoiceInput input)
    {
        var client = await CheckInput(input);
        if (client is null) return ValidationProblem(ModelState);

        // Starts as Draft. The GST rate shown now is replaced with the current rate when it's sent.
        var invoice = new Invoice { OwnerId = UserId, Client = client, TaxRate = InvoiceMath.GstRate };
        Apply(invoice, input);
        db.Invoices.Add(invoice);
        await SaveWithNextNumber(invoice);
        return CreatedAtAction(nameof(GetOne), new { id = invoice.Id }, ToDto(invoice));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<InvoiceDto>> Update(Guid id, InvoiceInput input)
    {
        var invoice = await MyInvoices.Include(i => i.LineItems).FirstOrDefaultAsync(i => i.Id == id);
        if (invoice is null) return NotFound();

        // A sent invoice is a record of what the client received, so only drafts can change
        if (invoice.Status != InvoiceStatus.Draft)
            return Problem(statusCode: StatusCodes.Status409Conflict, title: "Only draft invoices can be edited.");

        var client = await CheckInput(input);
        if (client is null) return ValidationProblem(ModelState);

        invoice.Client = client;
        Apply(invoice, input); // the number and tax rate stay as they were
        invoice.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return ToDto(invoice);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var invoice = await MyInvoices.FirstOrDefaultAsync(i => i.Id == id);
        if (invoice is null) return NotFound();

        // Only drafts. A sent invoice is a record of what the client received:
        // it gets voided later (Week 5), never deleted.
        if (invoice.Status != InvoiceStatus.Draft)
            return Problem(statusCode: StatusCodes.Status409Conflict, title: "Only draft invoices can be deleted.");

        db.Invoices.Remove(invoice); // its line items go too (cascade)
        await db.SaveChangesAsync();
        return NoContent();
    }

    // Who the invoice is from (shared with the Stripe webhook, see Mail/Senders.cs)
    Task<InvoicePdf.Sender> SenderAsync() => db.SenderAsync(UserId);

    // Checks attributes can't do. Returns the client, or null with errors in ModelState.
    async Task<Client?> CheckInput(InvoiceInput input)
    {
        if (input.DueDate < input.IssueDate)
            ModelState.AddModelError(nameof(input.DueDate), "Due date can't be before the issue date.");

        // A huge amount gets a clear 400, instead of overflowing the database column (a 500)
        var subtotal = input.LineItems.Sum(li => InvoiceMath.LineAmount(li.Quantity, li.UnitPrice));
        if (subtotal > InvoiceMath.MaxSubtotal)
            ModelState.AddModelError(nameof(input.LineItems), "This invoice is too large. Split it into smaller invoices.");

        // The client must be YOURS. Otherwise anyone could put another user's client on an invoice.
        // Same message whether it doesn't exist or isn't yours, so nothing leaks.
        var client = await db.Clients.FirstOrDefaultAsync(c => c.Id == input.ClientId && c.OwnerId == UserId);
        if (client is null)
            ModelState.AddModelError(nameof(input.ClientId), "Choose one of your clients.");

        return ModelState.IsValid ? client : null;
    }

    // Next number for this user. If two requests pick the same number at the same moment,
    // the unique index rejects one of them, and that one simply takes the next number.
    async Task SaveWithNextNumber(Invoice invoice)
    {
        for (var attempt = 1; ; attempt++)
        {
            invoice.Number = (await MyInvoices.MaxAsync(i => (int?)i.Number) ?? 0) + 1;
            try
            {
                await db.SaveChangesAsync();
                return;
            }
            catch (DbUpdateException ex) when (attempt < MaxNumberAttempts && ex.InnerException is SqlException { Number: 2601 or 2627 })
            {
                // 2601/2627 = duplicate key: someone else just took this number, so go round again
            }
        }
    }

    static void Apply(Invoice invoice, InvoiceInput input)
    {
        invoice.IssueDate = input.IssueDate!.Value;
        invoice.DueDate = input.DueDate!.Value;
        invoice.Notes = string.IsNullOrWhiteSpace(input.Notes) ? null : input.Notes.Trim();

        // Replace the lines. EF deletes the old ones, because a line item can't exist without its invoice.
        invoice.LineItems.Clear();
        invoice.LineItems.AddRange(input.LineItems.Select((li, n) => new LineItem
        {
            Position = n + 1,
            Description = li.Description.Trim(),
            Quantity = li.Quantity,
            UnitPrice = li.UnitPrice,
        }));

        // Totals always come from the lines, never from the request
        InvoiceMath.Recalculate(invoice);
    }

    static InvoiceDto ToDto(Invoice i) => new(
        i.Id, InvoiceMath.FormatNumber(i.Number), i.ClientId, i.Client.Name, i.Status.ToString(), i.IssueDate, i.DueDate, i.Notes,
        i.Subtotal, i.TaxRate, i.Tax, i.Total,
        i.LineItems.OrderBy(li => li.Position)
            .Select(li => new LineItemDto(li.Id, li.Position, li.Description, li.Quantity, li.UnitPrice,
                InvoiceMath.LineAmount(li.Quantity, li.UnitPrice))).ToList(),
        i.CreatedAt, i.UpdatedAt, Utc(i.PaidAt), Utc(i.LastRemindedAt));

    // Stored as UTC. Marked as UTC so the browser shows it in the user's own time.
    static DateTime? Utc(DateTime? d) => d is { } v ? DateTime.SpecifyKind(v, DateTimeKind.Utc) : null;
}