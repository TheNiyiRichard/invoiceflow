using Invoiceflow.Api.Auth;
using Invoiceflow.Api.Data;
using Invoiceflow.Api.Dtos;
using Invoiceflow.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Invoiceflow.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/clients")]
public class ClientsController(AppDbContext db, CurrentUser currentUser) : ControllerBase
{
    // [Authorize] guarantees someone is signed in, so this is never null here
    string UserId => currentUser.Id!;

    // Every query starts here: only the signed-in user's clients
    IQueryable<Client> MyClients => db.Clients.Where(c => c.OwnerId == UserId);

    [HttpGet]
    public async Task<List<ClientDto>> GetAll() =>
        await MyClients.AsNoTracking()
            .OrderBy(c => c.Name)
            .Select(c => new ClientDto(c.Id, c.Name, c.Email, c.Company, c.Address, c.CreatedAt))
            .ToListAsync();

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ClientDto>> GetOne(Guid id)
    {
        var client = await MyClients.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id);
        return client is null ? NotFound() : ToDto(client);
    }

    [HttpPost]
    public async Task<ActionResult<ClientDto>> Create(ClientInput input)
    {
        var client = new Client { Id = Guid.NewGuid(), OwnerId = UserId };
        Apply(client, input);
        db.Clients.Add(client);
        await db.SaveChangesAsync();
        return CreatedAtAction(nameof(GetOne), new { id = client.Id }, ToDto(client));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<ClientDto>> Update(Guid id, ClientInput input)
    {
        var client = await MyClients.FirstOrDefaultAsync(c => c.Id == id);
        if (client is null) return NotFound();
        Apply(client, input);
        await db.SaveChangesAsync();
        return ToDto(client);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var client = await MyClients.FirstOrDefaultAsync(c => c.Id == id);
        if (client is null) return NotFound();

        // Invoices are financial records, so their client can't disappear
        if (await db.Invoices.AnyAsync(i => i.ClientId == id))
            return Problem(statusCode: StatusCodes.Status409Conflict, title: "This client has invoices, so it can't be deleted.");

        db.Clients.Remove(client);
        await db.SaveChangesAsync();
        return NoContent();
    }

    static void Apply(Client c, ClientInput i)
    {
        c.Name = i.Name.Trim();
        c.Email = i.Email.Trim().ToLowerInvariant();
        c.Company = string.IsNullOrWhiteSpace(i.Company) ? null : i.Company.Trim();
        c.Address = string.IsNullOrWhiteSpace(i.Address) ? null : i.Address.Trim();
    }

    static ClientDto ToDto(Client c) => new(c.Id, c.Name, c.Email, c.Company, c.Address, c.CreatedAt);
}