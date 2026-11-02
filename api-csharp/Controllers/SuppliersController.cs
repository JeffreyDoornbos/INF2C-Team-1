using CargoHub.Api.Data;
using CargoHub.Api.Domain;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CargoHub.Api.Controllers;

[ApiController]
[Route("api/v1/suppliers")]
public sealed class SuppliersController(CargoHubDbContext context) : ControllerBase
{
    // GET /api/v1/suppliers
    [HttpGet]
    public async Task<IActionResult> GetSuppliers(CancellationToken ct)
        => Ok(await context.Suppliers.AsNoTracking().OrderBy(x => x.Id).ToListAsync(ct));

    // GET /api/v1/suppliers/{id}, Python geeft 200 en JSON null bij onbekend ID.
    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetSupplier(int id, CancellationToken ct)
    {
        var entity = await context.Suppliers.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        return new JsonResult(entity) { StatusCode = 200 };
    }

    // POST /api/v1/suppliers, Python retourneert 201 zonder response-body.
    [HttpPost]
    public async Task<IActionResult> AddSupplier([FromBody] Supplier entity, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        entity.CreatedAt = now;
        entity.UpdatedAt = now;
        context.Suppliers.Add(entity);
        await context.SaveChangesAsync(ct);
        return StatusCode(201);
    }

    // PUT /api/v1/suppliers/{id}, Python retourneert 200 zonder response-body.
    [HttpPut("{id:int}")]
    public async Task<IActionResult> UpdateSupplier(int id, [FromBody] Supplier updated, CancellationToken ct)
    {
        var existing = await context.Suppliers.FirstOrDefaultAsync(x => x.Id == id, ct);
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
        existing.Code = updated.Code;
        existing.PhoneNumber = updated.PhoneNumber;
        existing.Reference = updated.Reference;
        existing.UpdatedAt = DateTimeOffset.UtcNow;
        await context.SaveChangesAsync(ct);
        return Ok();
    }

    // DELETE /api/v1/suppliers/{id}, Python retourneert 200, ook bij onbekend ID.
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> RemoveSupplier(int id, CancellationToken ct)
    {
        var entity = await context.Suppliers.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (entity is not null)
        {
            context.Suppliers.Remove(entity);
            await context.SaveChangesAsync(ct);
        }
        return Ok();
    }
}
