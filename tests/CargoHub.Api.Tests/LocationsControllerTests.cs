using CargoHub.Api.Controllers;
using CargoHub.Api.Data;
using CargoHub.Api.Domain;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CargoHub.Api.Tests;

public class LocationsControllerTests
{
    // Creates a new in-memory database for each test.
    private static CargoHubDbContext CreateDatabase()
    {
        var options = new DbContextOptionsBuilder<CargoHubDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new CargoHubDbContext(options);
    }

    // Creates a location with default test values.
    private static Location NewLocation(int id = 1) => new()
    {
        Id = id,
        WarehouseId = 1,
        Code = "LOC-001",
        Name = "Test Location"
    };

    // Tests if all locations are returned.
    [Fact]
    public async Task GetLocations_ReturnsAllLocations()
    {
        await using var db = CreateDatabase();

        // Add two locations to the database.
        db.Locations.AddRange(NewLocation(1), NewLocation(2));
        await db.SaveChangesAsync();

        var controller = new LocationsController(db);
        var action = await controller.GetLocations();

        // Check if both locations are returned.
        var result = Assert.IsType<OkObjectResult>(action.Result);
        var locations = Assert.IsAssignableFrom<IEnumerable<Location>>(result.Value);

        Assert.Equal(2, locations.Count());
    }

    // Tests if an existing location can be found by ID.
    [Fact]
    public async Task GetLocation_ReturnsExistingLocation()
    {
        await using var db = CreateDatabase();
        db.Locations.Add(NewLocation(1));
        await db.SaveChangesAsync();

        var controller = new LocationsController(db);
        var action = await controller.GetLocation(1);

        // Check if the correct location is returned.
        var result = Assert.IsType<OkObjectResult>(action.Result);
        var location = Assert.IsType<Location>(result.Value);

        Assert.Equal(1, location.Id);
        Assert.Equal("LOC-001", location.Code);
        Assert.Equal("Test Location", location.Name);
    }

    // Tests if an unknown location ID returns 404.
    [Fact]
    public async Task GetLocation_UnknownId_ReturnsNotFound()
    {
        await using var db = CreateDatabase();
        var controller = new LocationsController(db);

        var action = await controller.GetLocation(999);

        // Check if the controller returns NotFound.
        Assert.IsType<NotFoundResult>(action.Result);
    }

    // Tests if a new location can be created.
    [Fact]
    public async Task AddLocation_CreatesRecordAndReturns201()
    {
        await using var db = CreateDatabase();
        var controller = new LocationsController(db);

        var result = await controller.AddLocation(NewLocation());

        // Check if the controller returns HTTP 201 Created.
        var created = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(201, created.StatusCode);

        // Check if the location was saved with timestamps.
        var location = await db.Locations.SingleAsync();

        Assert.Equal(1, location.Id);
        Assert.Equal("LOC-001", location.Code);
        Assert.Equal("Test Location", location.Name);
        Assert.True(location.CreatedAt > DateTimeOffset.MinValue);
        Assert.True(location.UpdatedAt > DateTimeOffset.MinValue);
    }

    // Tests if an existing location can be updated.
    [Fact]
    public async Task UpdateLocation_UpdatesExistingRecord()
    {
        await using var db = CreateDatabase();
        db.Locations.Add(NewLocation(1));
        await db.SaveChangesAsync();

        var controller = new LocationsController(db);

        // Change the location properties.
        var updated = NewLocation(1);
        updated.WarehouseId = 2;
        updated.Code = "LOC-UPDATED";
        updated.Name = "Updated Location";

        var result = await controller.UpdateLocation(1, updated);

        // Check if the updated location is returned.
        var response = Assert.IsType<OkObjectResult>(result);
        var location = Assert.IsType<Location>(response.Value);

        Assert.Equal(2, location.WarehouseId);
        Assert.Equal("LOC-UPDATED", location.Code);
        Assert.Equal("Updated Location", location.Name);
        Assert.True(location.UpdatedAt > DateTimeOffset.MinValue);
    }

    // Tests if updating an unknown location returns 404.
    [Fact]
    public async Task UpdateLocation_UnknownId_ReturnsNotFound()
    {
        await using var db = CreateDatabase();
        var controller = new LocationsController(db);

        var result = await controller.UpdateLocation(999, NewLocation(999));

        // Check if no location was created.
        Assert.IsType<NotFoundResult>(result);
        Assert.Empty(await db.Locations.ToListAsync());
    }

    // Tests if an existing location can be deleted.
    [Fact]
    public async Task DeleteLocation_RemovesRecord()
    {
        await using var db = CreateDatabase();
        db.Locations.Add(NewLocation(1));
        await db.SaveChangesAsync();

        var controller = new LocationsController(db);
        var result = await controller.DeleteLocation(1);

        // Check if the location was deleted successfully.
        Assert.IsType<OkResult>(result);
        Assert.Empty(await db.Locations.ToListAsync());
    }

    // Tests if deleting an unknown location returns 404.
    [Fact]
    public async Task DeleteLocation_UnknownId_ReturnsNotFound()
    {
        await using var db = CreateDatabase();
        var controller = new LocationsController(db);

        var result = await controller.DeleteLocation(999);

        // Check if the controller returns NotFound.
        Assert.IsType<NotFoundResult>(result);
    }
}