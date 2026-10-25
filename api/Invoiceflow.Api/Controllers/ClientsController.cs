using Invoiceflow.Api.Data;
using Invoiceflow.Api.Dtos;
using Invoiceflow.Api.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Invoiceflow.Api.Controllers;

[ApiController]
[Route("api/clients")]
public class ClientsController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<List<ClientDto>> GetAll() =>
        await db.Clients.AsNoTracking()
            .OrderBy(c => c.Name)
            .Select(c => new ClientDto(c.Id, c.Name, c.Email, c.Company, c.Address, c.CreatedAt))
            .ToListAsync();

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ClientDto>> GetOne(Guid id)
    {
        var client = await db.Clients.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id);
        return client is null ? NotFound() : ToDto(client);
    }

    [HttpPost]
    public async Task<ActionResult<ClientDto>> Create(ClientInput input)
    {
        var client = new Client { Id = Guid.NewGuid() };
        Apply(client, input);
        db.Clients.Add(client);
        await db.SaveChangesAsync();
        return CreatedAtAction(nameof(GetOne), new { id = client.Id }, ToDto(client));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<ClientDto>> Update(Guid id, ClientInput input)
    {
        var client = await db.Clients.FindAsync(id);
        if (client is null) return NotFound();
        Apply(client, input);
        await db.SaveChangesAsync();
        return ToDto(client);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var client = await db.Clients.FindAsync(id);
        if (client is null) return NotFound();
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