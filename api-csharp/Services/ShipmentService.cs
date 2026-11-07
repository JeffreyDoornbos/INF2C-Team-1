using CargoHub.Api.Contracts;
using CargoHub.Api.Data;
using CargoHub.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace CargoHub.Api.Services;

public sealed class ShipmentValidationException(string message) : Exception(message);

public sealed class ShipmentService(CargoHubDbContext db, ShipmentItemStore lines)
{
    public async Task<List<ShipmentResponse>> GetShipmentsAsync(CancellationToken ct)
    {
        var shipments = await db.Set<Shipment>().AsNoTracking().OrderBy(x => x.Id).ToListAsync(ct);
        var items = await db.Set<ShipmentItem>().AsNoTracking().OrderBy(x => x.Id).ToListAsync(ct);
        var lookup = items.ToLookup(x => x.ShipmentId);
        return shipments.Select(x => WithItems(x, lookup[x.Id])).ToList();
    }

    public async Task<ShipmentResponse?> GetShipmentAsync(int id, CancellationToken ct)
    {
        var shipment = await db.Set<Shipment>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
        return shipment is null ? null : WithItems(shipment, await lines.GetItemsForShipmentAsync(id, ct));
    }

    public async Task<List<ShipmentLineResponse>> GetItemsInShipmentAsync(int id, CancellationToken ct)
        => (await lines.GetItemsForShipmentAsync(id, ct)).Select(x => new ShipmentLineResponse(x.ItemId, x.Amount)).ToList();

    public async Task<int[]> GetOrderIdsInShipmentAsync(int id, CancellationToken ct)
    {
        var orderId = await db.Set<Shipment>().Where(x => x.Id == id).Select(x => x.OrderId).SingleOrDefaultAsync(ct);
        return orderId.HasValue ? [orderId.Value] : [];
    }

    public async Task AddShipmentAsync(ShipmentWriteRequest request, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await ValidateReferencesAsync(request.OrderId, request.Items, ct);
        ValidateDate(request.ShipmentDate);
        var now = DateTimeOffset.UtcNow;
        var shipment = new Shipment { Id = request.Id, CreatedAt = now, UpdatedAt = now };
        CopyHeader(request, shipment);
        db.Add(shipment);
        await db.SaveChangesAsync(ct);
        // Python add_shipment uses set_items, without an ordered-stock adjustment.
        await lines.SetItemsForShipmentAsync(shipment.Id, request.Items ?? [], ct);
        await transaction.CommitAsync(ct);
    }

    public async Task UpdateShipmentAsync(int id, ShipmentWriteRequest request, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var shipment = await LockShipmentAsync(id, ct);
        if (shipment is null) return; // Preserve legacy 200/no-op for a missing ID.
        await ValidateReferencesAsync(request.OrderId, request.Items, ct);
        ValidateDate(request.ShipmentDate);
        CopyHeader(request, shipment);
        shipment.UpdatedAt = DateTimeOffset.UtcNow;
        if (request.Items is not null) await ReplaceItemsAndAdjustOrderedAsync(id, request.Items, ct);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }

    public async Task UpdateItemsInShipmentAsync(int id, IReadOnlyList<ShipmentLineRequest> items, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var shipment = await LockShipmentAsync(id, ct);
        if (shipment is null) throw new ShipmentValidationException("Shipment does not exist.");
        await ValidateReferencesAsync(null, items, ct);
        await ReplaceItemsAndAdjustOrderedAsync(id, items, ct);
        await transaction.CommitAsync(ct);
        // Python's dedicated items operation does not update the header timestamp.
    }

    public async Task UpdateOrdersInShipmentAsync(int id, IReadOnlyList<int> orderIds, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var shipment = await LockShipmentAsync(id, ct);
        if (shipment is null) return;
        // Legacy v1 takes the first entry, even when several IDs are supplied.
        var orderId = orderIds.Count == 0 ? (int?)null : orderIds[0];
        await ValidateReferencesAsync(orderId, null, ct);
        shipment.OrderId = orderId;
        shipment.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }

    public async Task RemoveShipmentAsync(int id, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var shipment = await LockShipmentAsync(id, ct);
        if (shipment is null) return;
        await lines.RemoveItemsForShipmentAsync(id, ct);
        db.Remove(shipment);
        await db.SaveChangesAsync(ct);
        // Legacy deletion removes lines but does not reverse ordered quantities.
        await transaction.CommitAsync(ct);
    }

    private Task<Shipment?> LockShipmentAsync(int id, CancellationToken ct)
        => db.Set<Shipment>().FromSqlInterpolated($"SELECT * FROM public.shipment WHERE id = {id} FOR UPDATE")
            .SingleOrDefaultAsync(ct);

    private async Task ReplaceItemsAndAdjustOrderedAsync(int id, IReadOnlyList<ShipmentLineRequest> items, CancellationToken ct)
    {
        var current = await lines.GetItemsForShipmentAsync(id, ct);
        // Python dict comprehensions keep the LAST amount for duplicate item IDs.
        // Preserve that calculation while keeping all submitted lines in storage.
        var oldByItem = current.GroupBy(x => x.ItemId).ToDictionary(g => g.Key, g => g.Last().Amount);
        var newByItem = items.GroupBy(x => x.ItemId).ToDictionary(g => g.Key, g => g.Last().Amount);
        var itemIds = oldByItem.Keys.Union(newByItem.Keys).Order().ToArray();
        if (itemIds.Length != 0)
        {
            var inventories = await db.Inventories.FromSqlInterpolated($"SELECT * FROM public.inventory WHERE item_id = ANY({itemIds}) ORDER BY item_id, location_id FOR UPDATE").ToListAsync(ct);
            foreach (var itemId in itemIds)
            {
                long delta = (long)newByItem.GetValueOrDefault(itemId) - oldByItem.GetValueOrDefault(itemId);
                if (delta != 0) ApplyOrderedDelta(inventories, itemId, delta);
            }
        }
        await db.SaveChangesAsync(ct);
        await lines.SetItemsForShipmentAsync(id, items, ct);
    }

    private static void ApplyOrderedDelta(IEnumerable<Inventory> inventories, int itemId, long delta)
    {
        var target = inventories.Where(x => x.ItemId == itemId)
            .OrderByDescending(x => x.QuantityOnHand).ThenBy(x => x.LocationId).FirstOrDefault();
        if (target is null) return;
        var next = Math.Max(0L, target.QuantityOrdered + delta);
        if (next > int.MaxValue) throw new ShipmentValidationException("Ordered quantity exceeds the supported range.");
        target.QuantityOrdered = (int)next;
        target.UpdatedAt = DateTimeOffset.UtcNow;
    }

    private async Task ValidateReferencesAsync(int? orderId, IReadOnlyList<ShipmentLineRequest>? items, CancellationToken ct)
    {
        if (orderId.HasValue && !await db.Set<Order>().AnyAsync(x => x.Id == orderId, ct))
            throw new ShipmentValidationException("The linked order does not exist.");
        if (items is null) return;
        if (items.Any(x => x is null || x.ItemId <= 0 || x.Amount <= 0)) throw new ShipmentValidationException("Item IDs and amounts must be positive integers.");
        var ids = items.Select(x => x.ItemId).Distinct().ToArray();
        if (await db.Items.CountAsync(x => ids.Contains(x.Id), ct) != ids.Length)
            throw new ShipmentValidationException("One or more shipment items do not exist.");
    }

    private static void ValidateDate(DateTimeOffset date)
    {
        if (date == default) throw new ShipmentValidationException("Shipment date is required.");
    }

    private static void CopyHeader(ShipmentWriteRequest source, Shipment target)
    {
        target.Reference = source.Reference;
        target.OrderId = source.OrderId;
        target.ShipmentDate = source.ShipmentDate.ToUniversalTime();
        target.ShipmentType = source.ShipmentType;
        target.ShipmentStatus = source.ShipmentStatus;
        target.CarrierName = source.CarrierName;
        target.ShippingMethod = source.ShippingMethod;
        target.PaymentType = source.PaymentType;
    }

    private static ShipmentResponse WithItems(Shipment shipment, IEnumerable<ShipmentItem> items)
        => new(shipment.Id, shipment.Reference, shipment.OrderId, shipment.ShipmentDate,
            shipment.ShipmentType, shipment.ShipmentStatus, shipment.CarrierName,
            shipment.ShippingMethod, shipment.PaymentType, shipment.CreatedAt, shipment.UpdatedAt,
            items.Select(x => new ShipmentLineResponse(x.ItemId, x.Amount)).ToList());
}
