using CargoHub.Api.Controllers;
using CargoHub.Api.Data;
using CargoHub.Api.Domain;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CargoHub.Api.Tests;

public class ItemsControllerTests
{
    // Creates a new in-memory database for each test.
    private static CargoHubDbContext CreateDatabase()
    {
        var options = new DbContextOptionsBuilder<CargoHubDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new CargoHubDbContext(options);
    }

    // Creates an item with default test values.
    private static Item NewItem(int id = 1) => new()
    {
        Id = id,
        Code = "ITEM-001",
        Description = "Test Item",
        Barcode = "123456789",
        ModelNumber = "MODEL-001",
        CommodityCode = "COM-001",
        UnitWeight = 2.5m,
        ItemLineId = 1,
        ItemGroupId = 1,
        ItemTypeId = 1,
        MinPurchaseQty = 5,
        CaseSize = 10,
        PackagingType = "Box",
        OrderMultiple = 5,
        SupplierId = 1,
        SupplierSku = "SUP-001"
    };

    // Creates an inventory record for testing item stock.
    private static Inventory NewInventory(
        int itemId = 1,
        int locationId = 1) => new()
    {
        ItemId = itemId,
        LocationId = locationId,
        QuantityOnHand = 100,
        QuantityExpected = 20,
        QuantityOrdered = 30,
        QuantityAllocated = 10
    };

    // Tests if all items are returned.
    [Fact]
    public async Task GetItems_ReturnsAllItems()
    {
        await using var db = CreateDatabase();

        // Add two items to the database.
        db.Items.Add(NewItem(1));
        db.Items.Add(NewItem(2));
        await db.SaveChangesAsync();

        var controller = new ItemsController(db);
        var action = await controller.GetItems(null, null, null, null);

        // Check if both items are returned.
        var result = Assert.IsType<OkObjectResult>(action.Result);
        var items = Assert.IsAssignableFrom<IEnumerable<Item>>(result.Value);

        Assert.Equal(2, items.Count());
    }

    // Tests if items can be filtered by item line.
    [Fact]
    public async Task GetItems_FilterByItemLine_ReturnsMatchingItems()
    {
        await using var db = CreateDatabase();

        // Add items with different item line IDs.
        var first = NewItem(1);
        var second = NewItem(2);
        second.ItemLineId = 2;

        db.Items.AddRange(first, second);
        await db.SaveChangesAsync();

        var controller = new ItemsController(db);
        var action = await controller.GetItems(1, null, null, null);

        // Check if only the matching item is returned.
        var result = Assert.IsType<OkObjectResult>(action.Result);
        var items = Assert.IsAssignableFrom<IEnumerable<Item>>(result.Value);

        Assert.Single(items);
        Assert.Equal(1, items.First().Id);
    }

    // Tests if items can be filtered by item group.
    [Fact]
    public async Task GetItems_FilterByItemGroup_ReturnsMatchingItems()
    {
        await using var db = CreateDatabase();

        // Add items with different group IDs.
        var first = NewItem(1);
        var second = NewItem(2);
        second.ItemGroupId = 2;

        db.Items.AddRange(first, second);
        await db.SaveChangesAsync();

        var controller = new ItemsController(db);
        var action = await controller.GetItems(null, 1, null, null);

        // Check if only the matching item is returned.
        var result = Assert.IsType<OkObjectResult>(action.Result);
        var items = Assert.IsAssignableFrom<IEnumerable<Item>>(result.Value);

        Assert.Single(items);
        Assert.Equal(1, items.First().Id);
    }

    // Tests if items can be filtered by item type.
    [Fact]
    public async Task GetItems_FilterByItemType_ReturnsMatchingItems()
    {
        await using var db = CreateDatabase();

        // Add items with different type IDs.
        var first = NewItem(1);
        var second = NewItem(2);
        second.ItemTypeId = 2;

        db.Items.AddRange(first, second);
        await db.SaveChangesAsync();

        var controller = new ItemsController(db);
        var action = await controller.GetItems(null, null, 1, null);

        // Check if only the matching item is returned.
        var result = Assert.IsType<OkObjectResult>(action.Result);
        var items = Assert.IsAssignableFrom<IEnumerable<Item>>(result.Value);

        Assert.Single(items);
        Assert.Equal(1, items.First().Id);
    }

    // Tests if items can be filtered by supplier.
    [Fact]
    public async Task GetItems_FilterBySupplier_ReturnsMatchingItems()
    {
        await using var db = CreateDatabase();

        // Add items with different supplier IDs.
        var first = NewItem(1);
        var second = NewItem(2);
        second.SupplierId = 2;

        db.Items.AddRange(first, second);
        await db.SaveChangesAsync();

        var controller = new ItemsController(db);
        var action = await controller.GetItems(null, null, null, 1);

        // Check if only the matching item is returned.
        var result = Assert.IsType<OkObjectResult>(action.Result);
        var items = Assert.IsAssignableFrom<IEnumerable<Item>>(result.Value);

        Assert.Single(items);
        Assert.Equal(1, items.First().Id);
    }

    // Tests if an existing item can be found by ID.
    [Fact]
    public async Task GetItem_ReturnsExistingItem()
    {
        await using var db = CreateDatabase();
        db.Items.Add(NewItem(1));
        await db.SaveChangesAsync();

        var controller = new ItemsController(db);
        var action = await controller.GetItem(1);

        // Check if the correct item is returned.
        var result = Assert.IsType<OkObjectResult>(action.Result);
        var item = Assert.IsType<Item>(result.Value);

        Assert.Equal(1, item.Id);
        Assert.Equal("Test Item", item.Description);
    }

    // Tests if an unknown item ID returns 404.
    [Fact]
    public async Task GetItem_UnknownId_ReturnsNotFound()
    {
        await using var db = CreateDatabase();
        var controller = new ItemsController(db);

        var action = await controller.GetItem(999);

        // Check if the controller returns NotFound.
        Assert.IsType<NotFoundResult>(action.Result);
    }

    // Tests if all inventory records for an item are returned.
    [Fact]
    public async Task GetInventoriesForItem_ReturnsMatchingInventories()
    {
        await using var db = CreateDatabase();

        // Add two items and inventory records for both.
        db.Items.AddRange(NewItem(1), NewItem(2));
        db.Inventories.AddRange(
            NewInventory(1, 1),
            NewInventory(1, 2),
            NewInventory(2, 1));

        await db.SaveChangesAsync();

        var controller = new ItemsController(db);
        var action = await controller.GetInventoriesForItem(1);

        // Check if only inventory records for item 1 are returned.
        var result = Assert.IsType<OkObjectResult>(action.Result);
        var inventories = Assert.IsAssignableFrom<IEnumerable<Inventory>>(result.Value);

        Assert.Equal(2, inventories.Count());
        Assert.All(inventories, inventory => Assert.Equal(1, inventory.ItemId));
    }

    // Tests if inventory lookup for an unknown item returns 404.
    [Fact]
    public async Task GetInventoriesForItem_UnknownId_ReturnsNotFound()
    {
        await using var db = CreateDatabase();
        var controller = new ItemsController(db);

        var action = await controller.GetInventoriesForItem(999);

        // Check if the controller returns NotFound.
        Assert.IsType<NotFoundResult>(action.Result);
    }

    // Tests if inventory totals are calculated correctly.
    [Fact]
    public async Task GetInventoryTotalsForItem_ReturnsCorrectTotals()
    {
        await using var db = CreateDatabase();

        // Add an item with two inventory records.
        db.Items.Add(NewItem(1));
        db.Inventories.AddRange(
            NewInventory(1, 1),
            new Inventory
            {
                ItemId = 1,
                LocationId = 2,
                QuantityOnHand = 200,
                QuantityExpected = 30,
                QuantityOrdered = 40,
                QuantityAllocated = 20
            });

        await db.SaveChangesAsync();

        var controller = new ItemsController(db);
        var result = await controller.GetInventoryTotalsForItem(1);

        // Check the calculated stock totals.
        var response = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(response.Value);

        var totals = response.Value;

        Assert.Equal(50, (int)totals.GetType()
            .GetProperty("total_expected")!.GetValue(totals)!);

        Assert.Equal(70, (int)totals.GetType()
            .GetProperty("total_ordered")!.GetValue(totals)!);

        Assert.Equal(30, (int)totals.GetType()
            .GetProperty("total_allocated")!.GetValue(totals)!);

        Assert.Equal(270, (int)totals.GetType()
            .GetProperty("total_available")!.GetValue(totals)!);
    }

    // Tests if inventory totals for an unknown item return 404.
    [Fact]
    public async Task GetInventoryTotalsForItem_UnknownId_ReturnsNotFound()
    {
        await using var db = CreateDatabase();
        var controller = new ItemsController(db);

        var result = await controller.GetInventoryTotalsForItem(999);

        // Check if the controller returns NotFound.
        Assert.IsType<NotFoundResult>(result);
    }

    // Tests if item IDs can be retrieved by item line.
    [Fact]
    public async Task GetItemsForItemLine_ReturnsMatchingItemIds()
    {
        await using var db = CreateDatabase();

        // Add items with different item line IDs.
        var first = NewItem(1);
        var second = NewItem(2);
        second.ItemLineId = 2;

        db.Items.AddRange(first, second);
        await db.SaveChangesAsync();

        var controller = new ItemsController(db);
        var action = await controller.GetItemsForItemLine(1);

        // Check if only the matching item ID is returned.
        var result = Assert.IsType<OkObjectResult>(action.Result);
        var itemIds = Assert.IsAssignableFrom<IEnumerable<int>>(result.Value);

        Assert.Equal(new[] { 1 }, itemIds);
    }

    // Tests if item IDs can be retrieved by item group.
    [Fact]
    public async Task GetItemsItemGroup_ReturnsMatchingItemIds()
    {
        await using var db = CreateDatabase();

        // Add items with different item group IDs.
        var first = NewItem(1);
        var second = NewItem(2);
        second.ItemGroupId = 2;

        db.Items.AddRange(first, second);
        await db.SaveChangesAsync();

        var controller = new ItemsController(db);
        var action = await controller.GetItemsItemGroup(1);

        // Check if only the matching item ID is returned.
        var result = Assert.IsType<OkObjectResult>(action.Result);
        var itemIds = Assert.IsAssignableFrom<IEnumerable<int>>(result.Value);

        Assert.Equal(new[] { 1 }, itemIds);
    }

    // Tests if item IDs can be retrieved by item type.
    [Fact]
    public async Task GetItemsForItemType_ReturnsMatchingItemIds()
    {
        await using var db = CreateDatabase();

        // Add items with different item type IDs.
        var first = NewItem(1);
        var second = NewItem(2);
        second.ItemTypeId = 2;

        db.Items.AddRange(first, second);
        await db.SaveChangesAsync();

        var controller = new ItemsController(db);
        var action = await controller.GetItemsForItemType(1);

        // Check if only the matching item ID is returned.
        var result = Assert.IsType<OkObjectResult>(action.Result);
        var itemIds = Assert.IsAssignableFrom<IEnumerable<int>>(result.Value);

        Assert.Equal(new[] { 1 }, itemIds);
    }

    // Tests if all items for a supplier are returned.
    [Fact]
    public async Task GetItemsForSupplier_ReturnsMatchingItems()
    {
        await using var db = CreateDatabase();

        // Add items with different supplier IDs.
        var first = NewItem(1);
        var second = NewItem(2);
        second.SupplierId = 2;

        db.Items.AddRange(first, second);
        await db.SaveChangesAsync();

        var controller = new ItemsController(db);
        var action = await controller.GetItemsForSupplier(1);

        // Check if only the matching supplier items are returned.
        var result = Assert.IsType<OkObjectResult>(action.Result);
        var items = Assert.IsAssignableFrom<IEnumerable<Item>>(result.Value);

        Assert.Single(items);
        Assert.Equal(1, items.First().Id);
    }

    // Tests if a new item can be created.
    [Fact]
    public async Task AddItem_CreatesRecordAndReturns201()
    {
        await using var db = CreateDatabase();
        var controller = new ItemsController(db);

        var result = await controller.AddItem(NewItem());

        // Check if the controller returns HTTP 201 Created.
        var created = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(201, created.StatusCode);

        // Check if the item was saved with timestamps.
        var item = await db.Items.SingleAsync();

        Assert.Equal(1, item.Id);
        Assert.Equal("Test Item", item.Description);
        Assert.True(item.CreatedAt > DateTimeOffset.MinValue);
        Assert.True(item.UpdatedAt > DateTimeOffset.MinValue);
    }

    // Tests if an existing item can be updated.
    [Fact]
    public async Task UpdateItem_UpdatesExistingRecord()
    {
        await using var db = CreateDatabase();
        db.Items.Add(NewItem(1));
        await db.SaveChangesAsync();

        var controller = new ItemsController(db);

        // Change some item properties.
        var updated = NewItem(1);
        updated.Description = "Updated Item";
        updated.Code = "ITEM-UPDATED";
        updated.SupplierId = 2;

        var result = await controller.UpdateItem(1, updated);

        // Check if the updated item is returned.
        var response = Assert.IsType<OkObjectResult>(result);
        var item = Assert.IsType<Item>(response.Value);

        Assert.Equal("Updated Item", item.Description);
        Assert.Equal("ITEM-UPDATED", item.Code);
        Assert.Equal(2, item.SupplierId);
    }

    // Tests if updating an unknown item returns 404.
    [Fact]
    public async Task UpdateItem_UnknownId_ReturnsNotFound()
    {
        await using var db = CreateDatabase();
        var controller = new ItemsController(db);

        var result = await controller.UpdateItem(999, NewItem(999));

        // Check if no item was created.
        Assert.IsType<NotFoundResult>(result);
        Assert.Empty(await db.Items.ToListAsync());
    }

    // Tests if an existing item can be deleted.
    [Fact]
    public async Task DeleteItem_RemovesRecord()
    {
        await using var db = CreateDatabase();
        db.Items.Add(NewItem(1));
        await db.SaveChangesAsync();

        var controller = new ItemsController(db);
        var result = await controller.DeleteItem(1);

        // Check if the item was deleted successfully.
        Assert.IsType<OkResult>(result);
        Assert.Empty(await db.Items.ToListAsync());
    }

    // Tests if deleting an unknown item returns 404.
    [Fact]
    public async Task DeleteItem_UnknownId_ReturnsNotFound()
    {
        await using var db = CreateDatabase();
        var controller = new ItemsController(db);

        var result = await controller.DeleteItem(999);

        // Check if the controller returns NotFound.
        Assert.IsType<NotFoundResult>(result);
    }
}