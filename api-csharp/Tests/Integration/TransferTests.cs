using System.Net;
using System.Net.Http.Json;
using CargoHub.Api.Domain;
using CargoHub.Api.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace CargoHub.Api.IntegrationTests.Integration;

/// <summary>Controleert de voorraadregels en transacties van transfers in een echte PostgreSQL-database.</summary>
[Collection("PostgreSQL API")]
public sealed class TransferTests(PostgresFixture fixture)
{
    /// <summary>
    /// Controleert dat commit 10 stuks verplaatst, reserveringen behoudt en een herhaalde commit niet opnieuw boekt.
    /// </summary>
    [Fact]
    public async Task Commit_TransfersStockOnceAndPreservesAllocation()
    {
        const int id = 100;
        await SeedTransferAsync(id);

        using var first = await fixture.Client.PutAsync($"/api/v1/transfers/{id}/commit", null);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal((90, 30, 20, "Processed"), await StockAsync(id));

        using var repeated = await fixture.Client.PutAsync($"/api/v1/transfers/{id}/commit", null);
        Assert.Equal(HttpStatusCode.OK, repeated.StatusCode);
        Assert.Equal((90, 30, 20, "Processed"), await StockAsync(id));
    }

    /// <summary>
    /// Controleert rollback van de volledige transfer wanneer één van twee artikelen onvoldoende voorraad heeft.
    /// De eerste artikelregel mag dan ook geen voorraad verplaatsen.
    /// </summary>
    [Fact]
    public async Task Commit_InsufficientStockOnSecondLine_Returns409WithoutPartialMovement()
    {
        const int id = 200;
        await SeedTransferAsync(id);
        await using (var db = fixture.CreateContext())
        {
            db.Items.Add(new Item
            {
                Id = 2, Code = "CI-NO-STOCK", SupplierId = 1,
                ItemGroupId = 1, ItemLineId = 1, ItemTypeId = 1
            });
            db.TransferItems.Add(new TransferItem { TransferId = id, ItemId = 2, Amount = 1 });
            await db.SaveChangesAsync();
        }

        using var response = await fixture.Client.PutAsync($"/api/v1/transfers/{id}/commit", null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal((100, 20, 20, "Scheduled"), await StockAsync(id));
        await using var verification = fixture.CreateContext();
        Assert.False(await verification.Inventories.AnyAsync(x => x.ItemId == 2));
    }

    /// <summary>
    /// Controleert dat twee gelijktijdige commits dezelfde transfer maar één keer verwerken.
    /// Dit test de PostgreSQL-vergrendeling en voorkomt dubbele afboeking.
    /// </summary>
    [Fact]
    public async Task ConcurrentCommits_BothReturn200AndMoveStockOnlyOnce()
    {
        const int id = 300;
        await SeedTransferAsync(id);

        var responses = await Task.WhenAll(
            fixture.Client.PutAsync($"/api/v1/transfers/{id}/commit", null),
            fixture.Client.PutAsync($"/api/v1/transfers/{id}/commit", null));
        try
        {
            Assert.All(responses, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
            Assert.Equal((90, 30, 20, "Processed"), await StockAsync(id));
        }
        finally
        {
            foreach (var response in responses)
                response.Dispose();
        }
    }

    /// <summary>Controleert dat de API een artikelregel met hoeveelheid nul afwijst met 400 zonder transfer op te slaan.</summary>
    [Fact]
    public async Task PostZeroAmount_Returns400WithoutSavingTransfer()
    {
        using var response = await fixture.Client.PostAsJsonAsync("/api/v1/transfers", new
        {
            id = 400, reference = "CI invalid", from_location_id = 1, to_location_id = 2,
            items = new[] { new { item_id = 1, amount = 0 } }
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await using var db = fixture.CreateContext();
        Assert.False(await db.Transfers.AnyAsync(x => x.Id == 400));
        Assert.False(await db.TransferItems.AnyAsync(x => x.TransferId == 400));
    }

    /// <summary>Controleert dat commit van een niet-bestaande transfer een duidelijke 404 geeft.</summary>
    [Fact]
    public async Task CommitUnknownTransfer_Returns404()
    {
        using var response = await fixture.Client.PutAsync("/api/v1/transfers/999999/commit", null);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private async Task SeedTransferAsync(int id)
    {
        await using var db = fixture.CreateContext();
        db.Locations.AddRange(
            new Location { Id = id + 10, WarehouseId = 1 },
            new Location { Id = id + 11, WarehouseId = 1 });
        db.Inventories.AddRange(
            new Inventory { ItemId = 1, LocationId = id + 10, QuantityOnHand = 100, QuantityAllocated = 20 },
            new Inventory { ItemId = 1, LocationId = id + 11, QuantityOnHand = 20 });
        db.Transfers.Add(new Transfer
        {
            Id = id, Reference = "CI-" + id, FromLocationId = id + 10,
            ToLocationId = id + 11, TransferStatus = "Scheduled"
        });
        db.TransferItems.Add(new TransferItem { TransferId = id, ItemId = 1, Amount = 10 });
        await db.SaveChangesAsync();
    }

    private async Task<(int Source, int Destination, int Allocated, string Status)> StockAsync(int id)
    {
        await using var db = fixture.CreateContext();
        var source = await db.Inventories.AsNoTracking().SingleAsync(x => x.ItemId == 1 && x.LocationId == id + 10);
        var destination = await db.Inventories.AsNoTracking().SingleAsync(x => x.ItemId == 1 && x.LocationId == id + 11);
        var transfer = await db.Transfers.AsNoTracking().SingleAsync(x => x.Id == id);
        return (source.QuantityOnHand, destination.QuantityOnHand, source.QuantityAllocated, transfer.TransferStatus);
    }
}
