using CargoHub.Api.Data;
using CargoHub.Api.Domain;
using CargoHub.Api.Controllers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace CargoHub.Api.Tests;

public sealed class OrdersMigrationTests
{
    private static CargoHubDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<CargoHubDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new CargoHubDbContext(options);
    }

    private static Order SampleOrder(int id = 1) => new()
    {
        Id = id, ClientId = 10, ShipToClientId = 20, BillToClientId = 30,
        WarehouseId = 1, OrderDate = new DateOnly(2026, 11, 2),
        RequestDate = new DateOnly(2026, 11, 3), OrderStatus = "Pending",
        CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
    };

    [Fact]
    public async Task GetOrder_UnknownId_ReturnsNull()
    {
        await using var db = CreateDb();
        Assert.Null(await new OrderService(db).GetOrderAsync(999));
    }

    [Fact]
    public async Task GetOrder_IncludesItemsButNotUnitPrice()
    {
        await using var db = CreateDb();
        db.Orders.Add(SampleOrder());
        db.OrderItems.Add(new OrderItem { Id = 1, OrderId = 1, ItemId = 55, Amount = 4, UnitPrice = 3m });
        await db.SaveChangesAsync();
        var result = await new OrderService(db).GetOrderAsync(1);
        Assert.NotNull(result);
        Assert.Single(result.Items);
        Assert.Equal(55, result.Items[0].ItemId);
        Assert.Equal(4, result.Items[0].Amount);
    }

    [Theory]
    [InlineData(10)]
    [InlineData(20)]
    [InlineData(30)]
    public async Task GetOrdersForClient_MatchesAllThreeClientFields(int clientId)
    {
        await using var db = CreateDb();
        db.Orders.Add(SampleOrder());
        await db.SaveChangesAsync();
        Assert.Single(await new OrderService(db).GetOrdersAsync(clientId));
    }

    [Fact]
    public async Task GetItems_UnknownOrder_ReturnsEmptyList()
    {
        await using var db = CreateDb();
        Assert.Empty(await new OrderService(db).GetItemsAsync(999));
    }

    [Fact]
    public async Task ReplaceItems_IncreasesAllocationOnLocationWithMostStock()
    {
        await using var db = CreateDb();
        db.Orders.Add(SampleOrder());
        db.OrderItems.Add(new OrderItem { Id = 1, OrderId = 1, ItemId = 7, Amount = 2 });
        db.Inventories.AddRange(
            new Inventory { ItemId = 7, LocationId = 1, QuantityOnHand = 10, QuantityAllocated = 1 },
            new Inventory { ItemId = 7, LocationId = 2, QuantityOnHand = 50, QuantityAllocated = 3 });
        await db.SaveChangesAsync();
        await new OrderService(db).ReplaceItemsAsync(1, [new OrderItemWriteRequest { ItemId = 7, Amount = 5 }]);
        Assert.Equal(1, db.Inventories.Single(x => x.LocationId == 1).QuantityAllocated);
        Assert.Equal(6, db.Inventories.Single(x => x.LocationId == 2).QuantityAllocated);
        Assert.Equal(5, db.OrderItems.Single().Amount);
    }

    [Fact]
    public async Task ReplaceItems_RemovingItem_ClampsAllocationToZero()
    {
        await using var db = CreateDb();
        db.Orders.Add(SampleOrder());
        db.OrderItems.Add(new OrderItem { Id = 1, OrderId = 1, ItemId = 7, Amount = 10 });
        db.Inventories.Add(new Inventory { ItemId = 7, LocationId = 1, QuantityOnHand = 50, QuantityAllocated = 3 });
        await db.SaveChangesAsync();
        await new OrderService(db).ReplaceItemsAsync(1, []);
        Assert.Equal(0, db.Inventories.Single().QuantityAllocated);
        Assert.Empty(db.OrderItems);
    }

    [Fact]
    public async Task RemoveOrder_RemovesItemsWithoutReversingAllocation()
    {
        await using var db = CreateDb();
        db.Orders.Add(SampleOrder());
        db.OrderItems.Add(new OrderItem { Id = 1, OrderId = 1, ItemId = 7, Amount = 2 });
        db.Inventories.Add(new Inventory { ItemId = 7, LocationId = 1, QuantityOnHand = 10, QuantityAllocated = 5 });
        await db.SaveChangesAsync();
        await new OrderService(db).RemoveOrderAsync(1);
        Assert.Empty(db.Orders);
        Assert.Empty(db.OrderItems);
        Assert.Equal(5, db.Inventories.Single().QuantityAllocated);
    }
}
