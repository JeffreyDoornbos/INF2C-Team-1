using CargoHub.Api.Controllers;
using CargoHub.Api.Data;
using CargoHub.Api.Domain;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CargoHub.Api.Tests;

public class WarehousesControllerTests
{
    // Creates a new in-memory database for each test.
    private static CargoHubDbContext CreateDatabase()
    {
        var options = new DbContextOptionsBuilder<CargoHubDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new CargoHubDbContext(options);
    }

    // Creates a warehouse with default test values.
    private static Warehouse NewWarehouse(int id = 1) => new()
    {
        Id = id,
        Code = "WH-001",
        Name = "Test Warehouse",
        Address = "Test Street 1",
        City = "Rotterdam",
        ZipCode = "3000AA",
        Province = "South Holland",
        Country = "Netherlands",
        ContactName = "Test Contact",
        ContactPhone = "0101234567",
        ContactEmail = "test@example.com"
    };

    // Creates a location linked to a warehouse.
    private static Location NewLocation(int id, int warehouseId) => new()
    {
        Id = id,
        WarehouseId = warehouseId,
        Code = $"LOC-{id:000}",
        Name = $"Test Location {id}"
    };

    // Tests if all warehouses are returned.
    [Fact]
    public async Task GetWarehouses_ReturnsAllWarehouses()
    {
        await using var db = CreateDatabase();

        // Add two warehouses to the database.
        db.Warehouses.AddRange(NewWarehouse(1), NewWarehouse(2));
        await db.SaveChangesAsync();

        var controller = new WarehousesController(db);
        var action = await controller.GetWarehouses();

        // Check if both warehouses are returned.
        var result = Assert.IsType<OkObjectResult>(action.Result);
        var warehouses = Assert.IsAssignableFrom<IEnumerable<Warehouse>>(result.Value);

        Assert.Equal(2, warehouses.Count());
    }

    // Tests if an existing warehouse can be found by ID.
    [Fact]
    public async Task GetWarehouse_ReturnsExistingWarehouse()
    {
        await using var db = CreateDatabase();
        db.Warehouses.Add(NewWarehouse(1));
        await db.SaveChangesAsync();

        var controller = new WarehousesController(db);
        var action = await controller.GetWarehouse(1);

        // Check if the correct warehouse is returned.
        var result = Assert.IsType<OkObjectResult>(action.Result);
        var warehouse = Assert.IsType<Warehouse>(result.Value);

        Assert.Equal(1, warehouse.Id);
        Assert.Equal("WH-001", warehouse.Code);
        Assert.Equal("Test Warehouse", warehouse.Name);
    }

    // Tests if an unknown warehouse ID returns 404.
    [Fact]
    public async Task GetWarehouse_UnknownId_ReturnsNotFound()
    {
        await using var db = CreateDatabase();
        var controller = new WarehousesController(db);

        var action = await controller.GetWarehouse(999);

        // Check if the controller returns NotFound.
        Assert.IsType<NotFoundResult>(action.Result);
    }

    // Tests if locations for a warehouse are returned.
    [Fact]
    public async Task GetLocationsInWarehouse_ReturnsMatchingLocations()
    {
        await using var db = CreateDatabase();

        // Add two warehouses and locations for both.
        db.Warehouses.AddRange(NewWarehouse(1), NewWarehouse(2));
        db.Locations.AddRange(
            NewLocation(1, 1),
            NewLocation(2, 1),
            NewLocation(3, 2));

        await db.SaveChangesAsync();

        var controller = new WarehousesController(db);
        var action = await controller.GetLocationsInWarehouse(1);

        // Check if only locations for warehouse 1 are returned.
        var result = Assert.IsType<OkObjectResult>(action.Result);
        var locations = Assert.IsAssignableFrom<IEnumerable<Location>>(result.Value);

        Assert.Equal(2, locations.Count());
        Assert.All(locations, location => Assert.Equal(1, location.WarehouseId));
    }

    // Tests if location lookup for an unknown warehouse returns 404.
    [Fact]
    public async Task GetLocationsInWarehouse_UnknownId_ReturnsNotFound()
    {
        await using var db = CreateDatabase();
        var controller = new WarehousesController(db);

        var action = await controller.GetLocationsInWarehouse(999);

        // Check if the controller returns NotFound.
        Assert.IsType<NotFoundResult>(action.Result);
    }

    // Tests if a new warehouse can be created.
    [Fact]
    public async Task AddWarehouse_CreatesRecordAndReturns201()
    {
        await using var db = CreateDatabase();
        var controller = new WarehousesController(db);

        var result = await controller.AddWarehouse(NewWarehouse());

        // Check if the controller returns HTTP 201 Created.
        var created = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(201, created.StatusCode);

        // Check if the warehouse was saved with timestamps.
        var warehouse = await db.Warehouses.SingleAsync();

        Assert.Equal(1, warehouse.Id);
        Assert.Equal("WH-001", warehouse.Code);
        Assert.Equal("Test Warehouse", warehouse.Name);
        Assert.True(warehouse.CreatedAt > DateTimeOffset.MinValue);
        Assert.True(warehouse.UpdatedAt > DateTimeOffset.MinValue);
    }

    // Tests if an existing warehouse can be updated.
    [Fact]
    public async Task UpdateWarehouse_UpdatesExistingRecord()
    {
        await using var db = CreateDatabase();
        db.Warehouses.Add(NewWarehouse(1));
        await db.SaveChangesAsync();

        var controller = new WarehousesController(db);

        // Change the warehouse properties.
        var updated = NewWarehouse(1);
        updated.Code = "WH-UPDATED";
        updated.Name = "Updated Warehouse";
        updated.Address = "New Street 2";
        updated.City = "Amsterdam";
        updated.ZipCode = "1000AA";
        updated.Province = "North Holland";
        updated.Country = "Netherlands";
        updated.ContactName = "New Contact";
        updated.ContactPhone = "0209876543";
        updated.ContactEmail = "new@example.com";

        var result = await controller.UpdateWarehouse(1, updated);

        // Check if the updated warehouse is returned.
        var response = Assert.IsType<OkObjectResult>(result);
        var warehouse = Assert.IsType<Warehouse>(response.Value);

        Assert.Equal("WH-UPDATED", warehouse.Code);
        Assert.Equal("Updated Warehouse", warehouse.Name);
        Assert.Equal("New Street 2", warehouse.Address);
        Assert.Equal("Amsterdam", warehouse.City);
        Assert.Equal("1000AA", warehouse.ZipCode);
        Assert.Equal("North Holland", warehouse.Province);
        Assert.Equal("Netherlands", warehouse.Country);
        Assert.Equal("New Contact", warehouse.ContactName);
        Assert.Equal("0209876543", warehouse.ContactPhone);
        Assert.Equal("new@example.com", warehouse.ContactEmail);
        Assert.True(warehouse.UpdatedAt > DateTimeOffset.MinValue);
    }

    // Tests if updating an unknown warehouse returns 404.
    [Fact]
    public async Task UpdateWarehouse_UnknownId_ReturnsNotFound()
    {
        await using var db = CreateDatabase();
        var controller = new WarehousesController(db);

        var result = await controller.UpdateWarehouse(999, NewWarehouse(999));

        // Check if no warehouse was created.
        Assert.IsType<NotFoundResult>(result);
        Assert.Empty(await db.Warehouses.ToListAsync());
    }

    // Tests if an existing warehouse can be deleted.
    [Fact]
    public async Task DeleteWarehouse_RemovesRecord()
    {
        await using var db = CreateDatabase();
        db.Warehouses.Add(NewWarehouse(1));
        await db.SaveChangesAsync();

        var controller = new WarehousesController(db);
        var result = await controller.DeleteWarehouse(1);

        // Check if the warehouse was deleted successfully.
        Assert.IsType<OkResult>(result);
        Assert.Empty(await db.Warehouses.ToListAsync());
    }

    // Tests if deleting an unknown warehouse returns 404.
    [Fact]
    public async Task DeleteWarehouse_UnknownId_ReturnsNotFound()
    {
        await using var db = CreateDatabase();
        var controller = new WarehousesController(db);

        var result = await controller.DeleteWarehouse(999);

        // Check if the controller returns NotFound.
        Assert.IsType<NotFoundResult>(result);
    }
}