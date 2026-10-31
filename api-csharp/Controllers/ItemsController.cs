using CargoHub.Api.Data;
using CargoHub.Api.Domain;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CargoHub.Api.Controllers;

// Handles items endpoints, inventory calculations per item, and item filtering
[ApiController]
public class ItemsController : ControllerBase
{
    private readonly CargoHubDbContext _context;

    // Database context is provided via dependency injection
    public ItemsController(CargoHubDbContext context)
    {
        _context = context;
    }

    // GET /api/v1/items - Retrieve all items with optional query parameter filtering
    [HttpGet("api/v1/items")]
    public async Task<ActionResult<IEnumerable<Item>>> GetItems(
        [FromQuery(Name = "item_line")] int? itemLineId,
        [FromQuery(Name = "item_group")] int? itemGroupId,
        [FromQuery(Name = "item_type")] int? itemTypeId,
        [FromQuery(Name = "supplier")] int? supplierId)
    {
        var query = _context.Items.AsNoTracking().AsQueryable();

        // Apply filters if query parameters are provided
        if (itemLineId.HasValue)
        {
            query = query.Where(item => item.ItemLineId == itemLineId.Value);
        }

        if (itemGroupId.HasValue)
        {
            query = query.Where(item => item.ItemGroupId == itemGroupId.Value);
        }

        if (itemTypeId.HasValue)
        {
            query = query.Where(item => item.ItemTypeId == itemTypeId.Value);
        }

        if (supplierId.HasValue)
        {
            query = query.Where(item => item.SupplierId == supplierId.Value);
        }

        var items = await query.ToListAsync();
        return Ok(items);
    }

    // GET /api/v1/items/{id} - Retrieve a single item by ID
    [HttpGet("api/v1/items/{id:int}")]
    public async Task<ActionResult<Item>> GetItem(int id)
    {
        var item = await _context.Items.FindAsync(id);
        if (item == null)
        {
            return NotFound();
        }

        return Ok(item);
    }

    // GET /api/v1/items/{id}/inventory - Retrieve all inventory rows for a specific item
    [HttpGet("api/v1/items/{id:int}/inventory")]
    public async Task<ActionResult<IEnumerable<Inventory>>> GetInventoriesForItem(int id)
    {
        var itemExists = await _context.Items.AnyAsync(i => i.Id == id);
        if (!itemExists)
        {
            return NotFound();
        }

        var inventories = await _context.Inventories
            .Where(inv => inv.ItemId == id)
            .AsNoTracking()
            .ToListAsync();

        return Ok(inventories);
    }

    // GET /api/v1/items/{id}/inventory/totals - Calculate aggregated stock totals for an item
    [HttpGet("api/v1/items/{id:int}/inventory/totals")]
    public async Task<IActionResult> GetInventoryTotalsForItem(int id)
    {
        var itemExists = await _context.Items.AnyAsync(i => i.Id == id);
        if (!itemExists)
        {
            return NotFound();
        }

        var inventories = await _context.Inventories
            .Where(inv => inv.ItemId == id)
            .AsNoTracking()
            .ToListAsync();

        // Match Python total calculation logic
        int totalExpected = 0;
        int totalOrdered = 0;
        int totalAllocated = 0;
        int totalAvailable = 0;

        foreach (var inv in inventories)
        {
            totalExpected += inv.QuantityExpected;
            totalOrdered += inv.QuantityOrdered;
            totalAllocated += inv.QuantityAllocated;
            totalAvailable += (inv.QuantityOnHand - inv.QuantityAllocated);
        }

        return Ok(new
        {
            total_expected = totalExpected,
            total_ordered = totalOrdered,
            total_allocated = totalAllocated,
            total_available = totalAvailable
        });
    }

    // GET /api/v1/item_lines/{id}/items - Python returns a list of item IDs for a given item line
    [HttpGet("api/v1/item_lines/{id:int}/items")]
    public async Task<ActionResult<IEnumerable<int>>> GetItemsForItemLine(int id)
    {
        var itemIds = await _context.Items
            .Where(i => i.ItemLineId == id)
            .Select(i => i.Id)
            .ToListAsync();

        return Ok(itemIds);
    }

    // GET /api/v1/item_groups/{id}/items - Python returns a list of item IDs for a given item group
    [HttpGet("api/v1/item_groups/{id:int}/items")]
    public async Task<ActionResult<IEnumerable<int>>> GetItemsItemGroup(int id)
    {
        var itemIds = await _context.Items
            .Where(i => i.ItemGroupId == id)
            .Select(i => i.Id)
            .ToListAsync();

        return Ok(itemIds);
    }

    // GET /api/v1/item_types/{id}/items - Python returns a list of item IDs for a given item type
    [HttpGet("api/v1/item_types/{id:int}/items")]
    public async Task<ActionResult<IEnumerable<int>>> GetItemsForItemType(int id)
    {
        var itemIds = await _context.Items
            .Where(i => i.ItemTypeId == id)
            .Select(i => i.Id)
            .ToListAsync();

        return Ok(itemIds);
    }

    // GET /api/v1/suppliers/{id}/items - Python returns complete item objects for a given supplier
    [HttpGet("api/v1/suppliers/{id:int}/items")]
    public async Task<ActionResult<IEnumerable<Item>>> GetItemsForSupplier(int id)
    {
        var items = await _context.Items
            .Where(i => i.SupplierId == id)
            .AsNoTracking()
            .ToListAsync();

        return Ok(items);
    }

    // POST /api/v1/items - Create a new item
    [HttpPost("api/v1/items")]
    public async Task<ActionResult<Item>> AddItem([FromBody] Item item)
    {
        // Set creation and modification timestamps
        item.CreatedAt = DateTimeOffset.UtcNow;
        item.UpdatedAt = DateTimeOffset.UtcNow;

        _context.Items.Add(item);
        await _context.SaveChangesAsync();

        // Return HTTP 201 Created to preserve Python route contract
        return StatusCode(201, item);
    }

    // PUT /api/v1/items/{id} - Update an existing item by ID
    [HttpPut("api/v1/items/{id:int}")]
    public async Task<IActionResult> UpdateItem(int id, [FromBody] Item updatedItem)
    {
        var existingItem = await _context.Items.FindAsync(id);
        if (existingItem == null)
        {
            return NotFound();
        }

        // Apply updated properties
        existingItem.Code = updatedItem.Code;
        existingItem.Description = updatedItem.Description;
        existingItem.Barcode = updatedItem.Barcode;
        existingItem.ModelNumber = updatedItem.ModelNumber;
        existingItem.CommodityCode = updatedItem.CommodityCode;
        existingItem.UnitWeight = updatedItem.UnitWeight;
        existingItem.ItemLineId = updatedItem.ItemLineId;
        existingItem.ItemGroupId = updatedItem.ItemGroupId;
        existingItem.ItemTypeId = updatedItem.ItemTypeId;
        existingItem.MinPurchaseQty = updatedItem.MinPurchaseQty;
        existingItem.CaseSize = updatedItem.CaseSize;
        existingItem.PackagingType = updatedItem.PackagingType;
        existingItem.OrderMultiple = updatedItem.OrderMultiple;
        existingItem.SupplierId = updatedItem.SupplierId;
        existingItem.SupplierSku = updatedItem.SupplierSku;
        existingItem.UpdatedAt = DateTimeOffset.UtcNow;

        await _context.SaveChangesAsync();

        return Ok(existingItem);
    }

    // DELETE /api/v1/items/{id} - Remove an item by ID
    [HttpDelete("api/v1/items/{id:int}")]
    public async Task<IActionResult> DeleteItem(int id)
    {
        var item = await _context.Items.FindAsync(id);
        if (item == null)
        {
            return NotFound();
        }

        _context.Items.Remove(item);
        await _context.SaveChangesAsync();

        return Ok();
    }
}