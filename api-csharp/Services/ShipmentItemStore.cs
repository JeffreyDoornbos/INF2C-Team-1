using CargoHub.Api.Contracts;
using CargoHub.Api.Data;
using CargoHub.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace CargoHub.Api.Services;

public sealed class ShipmentItemStore(CargoHubDbContext db)
{
    public Task<List<ShipmentItem>> GetItemsForShipmentAsync(int shipmentId, CancellationToken ct = default)
        => db.Set<ShipmentItem>().Where(x => x.ShipmentId == shipmentId).OrderBy(x => x.Id).ToListAsync(ct);

    public async Task SetItemsForShipmentAsync(int shipmentId, IReadOnlyList<ShipmentLineRequest> items, CancellationToken ct = default)
    {
        await RemoveItemsForShipmentAsync(shipmentId, ct);
        db.Set<ShipmentItem>().AddRange(items.Select(x => new ShipmentItem
            { ShipmentId = shipmentId, ItemId = x.ItemId, Amount = x.Amount }));
        await db.SaveChangesAsync(ct);
    }

    public async Task RemoveItemsForShipmentAsync(int shipmentId, CancellationToken ct = default)
    {
        var rows = await GetItemsForShipmentAsync(shipmentId, ct);
        db.RemoveRange(rows);
        await db.SaveChangesAsync(ct);
    }
    // PostgreSQL identity replaces Python's global max(id)+1 and avoids ID races.
    // The caller owns the transaction so headers, lines and inventory commit together.
}
