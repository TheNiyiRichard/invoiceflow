using Invoiceflow.Api.Auth;
using Invoiceflow.Api.Data;
using Invoiceflow.Api.Dtos;
using Invoiceflow.Api.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Invoiceflow.Api.Controllers;

// No [Authorize] here on purpose: sign-in is required by default (Day 15),
// and EndpointAuthTests proves these routes refuse signed-out requests.
[ApiController]
[Route("api/invoices")]
public class InvoicesController(AppDbContext db, CurrentUser currentUser) : ControllerBase
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

    [HttpPost]
    public async Task<ActionResult<InvoiceDto>> Create(InvoiceInput input)
    {
        var client = await CheckInput(input);
        if (client is null) return ValidationProblem(ModelState);

        // Starts as Draft, with today's GST rate saved on it
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
        i.CreatedAt, i.UpdatedAt);
}