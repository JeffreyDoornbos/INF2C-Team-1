using CargoHub.Api.Data;
using CargoHub.Api.Domain;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CargoHub.Api.Controllers;


[ApiController]
[Route("api/v1/clients")]
public sealed class ClientsController(CargoHubDbContext context) : ControllerBase
{
    // GET /api/v1/clients
    [HttpGet]
    public async Task<IActionResult> GetClients(CancellationToken ct)
        => Ok(await context.Clients.AsNoTracking().OrderBy(x => x.Id).ToListAsync(ct));

    // GET /api/v1/clients/{id}, Python geeft 200 en JSON null bij onbekend ID.
    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetClient(int id, CancellationToken ct)
    {
        var entity = await context.Clients.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        return new JsonResult(entity) { StatusCode = 200 };
    }

    // POST /api/v1/clients, Python retourneert 201 zonder response body.
    [HttpPost]
    public async Task<IActionResult> AddClient([FromBody] Client entity, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        entity.CreatedAt = now;
        entity.UpdatedAt = now;
        context.Clients.Add(entity);
        await context.SaveChangesAsync(ct);
        return StatusCode(201);
    }

    // PUT /api/v1/clients/{id}, Python retourneert 200 zonder response body.
    [HttpPut("{id:int}")]
    public async Task<IActionResult> UpdateClient(int id, [FromBody] Client updated, CancellationToken ct)
    {
        var existing = await context.Clients.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (existing is null)
            return Ok(); // Python werkt bij onbekend ID niets bij, maar geeft 200.

        // Kopieer de velden, zonder de primaire sleutel of created_at te wijzigen.
        existing.Name = updated.Name;
        existing.Address = updated.Address;
        existing.City = updated.City;
        existing.ZipCode = updated.ZipCode;
        existing.Province = updated.Province;
        existing.Country = updated.Country;
        existing.ContactName = updated.ContactName;
        existing.ContactPhone = updated.ContactPhone;
        existing.ContactEmail = updated.ContactEmail;
        existing.UpdatedAt = DateTimeOffset.UtcNow;
        await context.SaveChangesAsync(ct);
        return Ok();
    }

    // DELETE /api/v1/clients/{id}, Python retourneert 200, ook bij onbekend ID.
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> RemoveClient(int id, CancellationToken ct)
    {
        var entity = await context.Clients.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (entity is not null)
        {
            context.Clients.Remove(entity);
            await context.SaveChangesAsync(ct);
        }
        return Ok();
    }
}
