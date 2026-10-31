using CargoHub.Api.Data;
using CargoHub.Api.Domain;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CargoHub.Api.Controllers;

// Base route for inventory endpoints, matching /api/v1/inventories
[ApiController]
[Route("api/v1/inventories")]
public class InventoriesController : ControllerBase
{
    private readonly CargoHubDbContext _context;

    // Database context is provided via dependency injection
    public InventoriesController(CargoHubDbContext context)
    {
        _context = context;
    }

    // GET /api/v1/inventories - Retrieve all inventory rows
    [HttpGet]
    public async Task<ActionResult<IEnumerable<Inventory>>> GetInventories()
    {
        var inventories = await _context.Inventories.AsNoTracking().ToListAsync();
        return Ok(inventories);
    }

    // GET /api/v1/inventories/{id} - Single ID lookup returns 404 matching Python legacy behavior
    [HttpGet("{id:int}")]
    public IActionResult GetInventoryById(int id)
    {
        // Inventory rows use a composite key (item_id, location_id); single surrogate id is not valid
        return NotFound();
    }

    // GET /api/v1/inventories/{itemId:int}/{locationId:int} - Retrieve specific inventory by composite key
    [HttpGet("{itemId:int}/{locationId:int}")]
    public async Task<ActionResult<Inventory>> GetInventory(int itemId, int locationId)
    {
        var inventory = await _context.Inventories.FindAsync(itemId, locationId);
        if (inventory == null)
        {
            return NotFound();
        }

        return Ok(inventory);
    }

    // POST /api/v1/inventories - Upsert inventory row based on composite key (item_id, location_id)
    [HttpPost]
    public async Task<ActionResult<Inventory>> AddInventory([FromBody] Inventory inventory)
    {
        var existing = await _context.Inventories.FindAsync(inventory.ItemId, inventory.LocationId);

        if (existing != null)
        {
            // Update existing stock quantities
            existing.QuantityOnHand = inventory.QuantityOnHand;
            existing.QuantityExpected = inventory.QuantityExpected;
            existing.QuantityOrdered = inventory.QuantityOrdered;
            existing.QuantityAllocated = inventory.QuantityAllocated;
            existing.UpdatedAt = DateTimeOffset.UtcNow;
        }
        else
        {
            // Populate timestamps and insert new inventory record
            inventory.CreatedAt = DateTimeOffset.UtcNow;
            inventory.UpdatedAt = DateTimeOffset.UtcNow;
            _context.Inventories.Add(inventory);
        }

        await _context.SaveChangesAsync();

        // Return HTTP 201 Created to match Python route behavior
        return StatusCode(201, existing ?? inventory);
    }

    // PUT /api/v1/inventories/{itemId:int}/{locationId:int} - Update inventory row by composite key
    [HttpPut("{itemId:int}/{locationId:int}")]
    public async Task<IActionResult> UpdateInventory(int itemId, int locationId, [FromBody] Inventory updatedInventory)
    {
        var existing = await _context.Inventories.FindAsync(itemId, locationId);
        if (existing == null)
        {
            return NotFound();
        }

        // Apply updated quantity values
        existing.QuantityOnHand = updatedInventory.QuantityOnHand;
        existing.QuantityExpected = updatedInventory.QuantityExpected;
        existing.QuantityOrdered = updatedInventory.QuantityOrdered;
        existing.QuantityAllocated = updatedInventory.QuantityAllocated;
        existing.UpdatedAt = DateTimeOffset.UtcNow;

        await _context.SaveChangesAsync();

        return Ok(existing);
    }

    // DELETE /api/v1/inventories/{itemId:int}/{locationId:int} - Remove inventory row by composite key
    [HttpDelete("{itemId:int}/{locationId:int}")]
    public async Task<IActionResult> DeleteInventory(int itemId, int locationId)
    {
        var inventory = await _context.Inventories.FindAsync(itemId, locationId);
        if (inventory == null)
        {
            return NotFound();
        }

        _context.Inventories.Remove(inventory);
        await _context.SaveChangesAsync();

        return Ok();
    }
}