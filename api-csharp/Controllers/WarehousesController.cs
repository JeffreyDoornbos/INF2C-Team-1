using CargoHub.Api.Data;
using CargoHub.Api.Domain;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CargoHub.Api.Controllers;

// Base route for warehouse endpoints, matching /api/v1/warehouses
[ApiController]
[Route("api/v1/warehouses")]
public class WarehousesController : ControllerBase
{
    private readonly CargoHubDbContext _context;

    // Database context is provided via dependency injection
    public WarehousesController(CargoHubDbContext context)
    {
        _context = context;
    }

    // GET /api/v1/warehouses - Retrieve all warehouses
    [HttpGet]
    public async Task<ActionResult<IEnumerable<Warehouse>>> GetWarehouses()
    {
        var warehouses = await _context.Warehouses.AsNoTracking().ToListAsync();
        return Ok(warehouses);
    }

    // GET /api/v1/warehouses/{id} - Retrieve a single warehouse by ID
    [HttpGet("{id:int}")]
    public async Task<ActionResult<Warehouse>> GetWarehouse(int id)
    {
        var warehouse = await _context.Warehouses.FindAsync(id);
        if (warehouse == null)
        {
            return NotFound();
        }

        return Ok(warehouse);
    }

    // GET /api/v1/warehouses/{id}/locations - Retrieve all locations associated with a warehouse
    [HttpGet("{id:int}/locations")]
    public async Task<ActionResult<IEnumerable<Location>>> GetLocationsInWarehouse(int id)
    {
        var warehouseExists = await _context.Warehouses.AnyAsync(w => w.Id == id);
        if (!warehouseExists)
        {
            return NotFound();
        }

        var locations = await _context.Locations
            .Where(l => l.WarehouseId == id)
            .AsNoTracking()
            .ToListAsync();

        return Ok(locations);
    }

    // POST /api/v1/warehouses - Create a new warehouse
    [HttpPost]
    public async Task<ActionResult<Warehouse>> AddWarehouse([FromBody] Warehouse warehouse)
    {
        // Populate timestamps on creation
        warehouse.CreatedAt = DateTimeOffset.UtcNow;
        warehouse.UpdatedAt = DateTimeOffset.UtcNow;

        _context.Warehouses.Add(warehouse);
        await _context.SaveChangesAsync();

        // Return HTTP 201 Created to match Python behavior
        return StatusCode(201, warehouse);
    }

    // PUT /api/v1/warehouses/{id} - Update an existing warehouse by ID
    [HttpPut("{id:int}")]
    public async Task<IActionResult> UpdateWarehouse(int id, [FromBody] Warehouse updatedWarehouse)
    {
        var existingWarehouse = await _context.Warehouses.FindAsync(id);
        if (existingWarehouse == null)
        {
            return NotFound();
        }

        // Apply updated field values
        existingWarehouse.Code = updatedWarehouse.Code;
        existingWarehouse.Name = updatedWarehouse.Name;
        existingWarehouse.Address = updatedWarehouse.Address;
        existingWarehouse.City = updatedWarehouse.City;
        existingWarehouse.ZipCode = updatedWarehouse.ZipCode;
        existingWarehouse.Province = updatedWarehouse.Province;
        existingWarehouse.Country = updatedWarehouse.Country;
        existingWarehouse.ContactName = updatedWarehouse.ContactName;
        existingWarehouse.ContactPhone = updatedWarehouse.ContactPhone;
        existingWarehouse.ContactEmail = updatedWarehouse.ContactEmail;
        existingWarehouse.UpdatedAt = DateTimeOffset.UtcNow;

        await _context.SaveChangesAsync();

        return Ok(existingWarehouse);
    }

    // DELETE /api/v1/warehouses/{id} - Delete a warehouse by ID
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> DeleteWarehouse(int id)
    {
        var warehouse = await _context.Warehouses.FindAsync(id);
        if (warehouse == null)
        {
            return NotFound();
        }

        _context.Warehouses.Remove(warehouse);
        await _context.SaveChangesAsync();

        return Ok();
    }
}