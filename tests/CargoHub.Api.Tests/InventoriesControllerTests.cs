using CargoHub.Api.Controllers;
using CargoHub.Api.Data;
using CargoHub.Api.Domain;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CargoHub.Api.Tests;

public class InventoriesControllerTests
{
    // Creates a new in-memory database for each test.
    private static CargoHubDbContext CreateDatabase()
    {
        var options = new DbContextOptionsBuilder<CargoHubDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new CargoHubDbContext(options);
    }

    // Creates an inventory record with default test values.
    private static Inventory NewInventory(int itemId = 1, int locationId = 1) => new()
    {
        ItemId = itemId,
        LocationId = locationId,
        QuantityOnHand = 100,
        QuantityExpected = 20,
        QuantityOrdered = 30,
        QuantityAllocated = 10
    };

    // Tests if all inventory records are returned.
    [Fact]
    public async Task GetInventories_ReturnsAllRecords()
    {
        await using var db = CreateDatabase();

        // Add two inventory records to the database.
        db.Inventories.Add(NewInventory());
        db.Inventories.Add(NewInventory(2, 2));
        await db.SaveChangesAsync();

        var controller = new InventoriesController(db);
        var action = await controller.GetInventories();

        // Check if the response contains both records.
        var result = Assert.IsType<OkObjectResult>(action.Result);
        var inventories = Assert.IsAssignableFrom<IEnumerable<Inventory>>(result.Value);

        Assert.Equal(2, inventories.Count());
    }

    // Tests if searching with one ID returns 404.
    [Fact]
    public async Task GetInventoryById_ReturnsNotFound()
    {
        await using var db = CreateDatabase();
        var controller = new InventoriesController(db);

        var result = controller.GetInventoryById(1);

        // A single ID cannot identify an inventory record.
        Assert.IsType<NotFoundResult>(result);
    }

    // Tests if an inventory record can be found using both key values.
    [Fact]
    public async Task GetInventory_ReturnsRecordByCompositeKey()
    {
        await using var db = CreateDatabase();

        // Add a record with item ID 1 and location ID 2.
        db.Inventories.Add(NewInventory(1, 2));
        await db.SaveChangesAsync();

        var controller = new InventoriesController(db);
        var action = await controller.GetInventory(1, 2);

        // Check if the correct inventory record is returned.
        var result = Assert.IsType<OkObjectResult>(action.Result);
        var inventory = Assert.IsType<Inventory>(result.Value);

        Assert.Equal(1, inventory.ItemId);
        Assert.Equal(2, inventory.LocationId);
    }

    // Tests if searching for an unknown inventory record returns 404.
    [Fact]
    public async Task GetInventory_UnknownCompositeKey_ReturnsNotFound()
    {
        await using var db = CreateDatabase();
        var controller = new InventoriesController(db);

        var action = await controller.GetInventory(999, 999);

        // Check if the controller returns NotFound.
        Assert.IsType<NotFoundResult>(action.Result);
    }

    // Tests if a new inventory record can be created.
    [Fact]
    public async Task AddInventory_CreatesRecordAndReturns201()
    {
        await using var db = CreateDatabase();
        var controller = new InventoriesController(db);

        var result = await controller.AddInventory(NewInventory());

        // Check if the controller returns HTTP 201 Created.
        var created = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(201, created.StatusCode);

        // Check if the record was saved with timestamps.
        var inventory = await db.Inventories.SingleAsync();

        Assert.Equal(1, inventory.ItemId);
        Assert.Equal(1, inventory.LocationId);
        Assert.True(inventory.CreatedAt > DateTimeOffset.MinValue);
        Assert.True(inventory.UpdatedAt > DateTimeOffset.MinValue);
    }

    // Tests if adding an existing record updates its quantities.
    [Fact]
    public async Task AddInventory_ExistingRecord_UpdatesQuantities()
    {
        await using var db = CreateDatabase();

        // Add an inventory record before updating it.
        var existing = NewInventory();
        existing.CreatedAt = DateTimeOffset.UtcNow.AddDays(-2);
        existing.UpdatedAt = DateTimeOffset.UtcNow.AddDays(-2);

        db.Inventories.Add(existing);
        await db.SaveChangesAsync();

        var controller = new InventoriesController(db);

        // Create new quantity values for the same inventory key.
        var updated = NewInventory();
        updated.QuantityOnHand = 200;
        updated.QuantityExpected = 40;
        updated.QuantityOrdered = 50;
        updated.QuantityAllocated = 25;

        var result = await controller.AddInventory(updated);

        // Check if the controller returns HTTP 201 Created.
        var created = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(201, created.StatusCode);

        // Check if the existing record was updated instead of duplicated.
        var inventory = await db.Inventories.SingleAsync();

        Assert.Equal(200, inventory.QuantityOnHand);
        Assert.Equal(40, inventory.QuantityExpected);
        Assert.Equal(50, inventory.QuantityOrdered);
        Assert.Equal(25, inventory.QuantityAllocated);
        Assert.Single(await db.Inventories.ToListAsync());
    }

    // Tests if an existing inventory record can be updated.
    [Fact]
    public async Task UpdateInventory_UpdatesExistingRecord()
    {
        await using var db = CreateDatabase();

        // Add an inventory record before updating it.
        db.Inventories.Add(NewInventory());
        await db.SaveChangesAsync();

        var controller = new InventoriesController(db);

        // Set the new quantity values.
        var updated = NewInventory();
        updated.QuantityOnHand = 250;
        updated.QuantityExpected = 60;
        updated.QuantityOrdered = 70;
        updated.QuantityAllocated = 30;

        var result = await controller.UpdateInventory(1, 1, updated);

        // Check if the updated record is returned successfully.
        var response = Assert.IsType<OkObjectResult>(result);
        var inventory = Assert.IsType<Inventory>(response.Value);

        Assert.Equal(250, inventory.QuantityOnHand);
        Assert.Equal(60, inventory.QuantityExpected);
        Assert.Equal(70, inventory.QuantityOrdered);
        Assert.Equal(30, inventory.QuantityAllocated);
    }

    // Tests if updating an unknown inventory record returns 404.
    [Fact]
    public async Task UpdateInventory_UnknownKey_ReturnsNotFound()
    {
        await using var db = CreateDatabase();
        var controller = new InventoriesController(db);

        var result = await controller.UpdateInventory(999, 999, NewInventory());

        // Check that no record was created.
        Assert.IsType<NotFoundResult>(result);
        Assert.Empty(await db.Inventories.ToListAsync());
    }

    // Tests if an existing inventory record can be deleted.
    [Fact]
    public async Task DeleteInventory_RemovesRecord()
    {
        await using var db = CreateDatabase();

        // Add a record before deleting it.
        db.Inventories.Add(NewInventory());
        await db.SaveChangesAsync();

        var controller = new InventoriesController(db);
        var result = await controller.DeleteInventory(1, 1);

        // Check if the record was deleted successfully.
        Assert.IsType<OkResult>(result);
        Assert.Empty(await db.Inventories.ToListAsync());
    }

    // Tests if deleting an unknown inventory record returns 404.
    [Fact]
    public async Task DeleteInventory_UnknownKey_ReturnsNotFound()
    {
        await using var db = CreateDatabase();
        var controller = new InventoriesController(db);

        var result = await controller.DeleteInventory(999, 999);

        // Check if the controller returns NotFound.
        Assert.IsType<NotFoundResult>(result);
    }
}