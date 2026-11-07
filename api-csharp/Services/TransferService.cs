using CargoHub.Api.Contracts;
using CargoHub.Api.Data;
using CargoHub.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace CargoHub.Api.Services;

public sealed class TransferOperationException(int statusCode, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}

public sealed class TransferService(CargoHubDbContext db, TransferItemStore lines, ILogger<TransferService> logger)
{
    public async Task<List<TransferResponse>> GetTransfersAsync(CancellationToken ct)
    {
        var records = await db.Transfers.AsNoTracking().OrderBy(x => x.Id).ToListAsync(ct);
        var items = (await db.TransferItems.AsNoTracking().OrderBy(x => x.Id).ToListAsync(ct)).ToLookup(x => x.TransferId);
        return records.Select(x => WithItems(x, items[x.Id])).ToList();
    }

    public async Task<TransferResponse?> GetTransferAsync(int id, CancellationToken ct)
    {
        var transfer = await db.Transfers.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
        return transfer is null ? null : WithItems(transfer, await lines.GetItemsForTransferAsync(id, ct));
    }

    public async Task<List<TransferLineResponse>> GetItemsInTransferAsync(int id, CancellationToken ct)
        => (await lines.GetItemsForTransferAsync(id, ct)).Select(x => new TransferLineResponse(x.ItemId, x.Amount)).ToList();

    public async Task AddTransferAsync(TransferWriteRequest request, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await ValidateAsync(request, request.Items ?? [], ct);
        var now = DateTimeOffset.UtcNow;
        var transfer = new Transfer { Id = request.Id, Reference = request.Reference,
            FromLocationId = request.FromLocationId, ToLocationId = request.ToLocationId,
            TransferStatus = "Scheduled", CreatedAt = now, UpdatedAt = now };
        db.Transfers.Add(transfer);
        await db.SaveChangesAsync(ct);
        await lines.SetItemsForTransferAsync(transfer.Id, request.Items!, ct);
        await transaction.CommitAsync(ct);
        logger.LogInformation("Scheduled batch transfer {TransferId}", transfer.Id);
    }

    public async Task UpdateTransferAsync(int id, TransferWriteRequest request, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var transfer = await LockTransferAsync(id, ct);
        if (transfer is null) return; // Legacy header PUT is a 200/no-op for an unknown ID.
        EnsureScheduled(transfer);
        if (request.TransferStatus != "Scheduled")
            throw new TransferOperationException(409, "A transfer can only become Processed through commit.");
        var current = await lines.GetItemsForTransferAsync(id, ct);
        var items = request.Items ?? current.Select(x => new TransferLineRequest { ItemId = x.ItemId, Amount = x.Amount }).ToList();
        await ValidateAsync(request, items, ct);
        transfer.Reference = request.Reference;
        transfer.FromLocationId = request.FromLocationId;
        transfer.ToLocationId = request.ToLocationId;
        transfer.UpdatedAt = DateTimeOffset.UtcNow;
        if (request.Items is not null) await lines.SetItemsForTransferAsync(id, request.Items, ct);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }

    public async Task RemoveTransferAsync(int id, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var transfer = await LockTransferAsync(id, ct);
        if (transfer is null) return;
        EnsureScheduled(transfer);
        await lines.RemoveItemsForTransferAsync(id, ct);
        db.Transfers.Remove(transfer);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }

    public async Task CommitTransferAsync(int id, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var transfer = await LockTransferAsync(id, ct)
            ?? throw new TransferOperationException(404, "Transfer does not exist.");
        if (transfer.TransferStatus == "Processed") return; // Idempotent repeat: no stock update or second notification.
        EnsureScheduled(transfer);
        var items = await lines.GetItemsForTransferAsync(id, ct);
        if (items.Count == 0) throw new TransferOperationException(409, "A transfer needs at least one item before commit.");
        var itemIds = items.Select(x => x.ItemId).Distinct().Order().ToArray();
        // Also serialize the case where the destination inventory row does not exist yet.
        // This advisory namespace is reserved for transfer item locks; acquire in item-ID order.
        foreach (var itemId in itemIds)
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(724210, {itemId})", ct);
        var locationIds = new[] { transfer.FromLocationId, transfer.ToLocationId };
        var inventories = await db.Inventories.FromSqlInterpolated($"SELECT * FROM public.inventory WHERE item_id = ANY({itemIds}) AND location_id = ANY({locationIds}) ORDER BY item_id, location_id FOR UPDATE").ToListAsync(ct);
        var now = DateTimeOffset.UtcNow;
        foreach (var item in items)
        {
            var source = inventories.SingleOrDefault(x => x.ItemId == item.ItemId && x.LocationId == transfer.FromLocationId);
            if (item.Amount <= 0 || source is null || (long)source.QuantityOnHand - source.QuantityAllocated < item.Amount)
                throw new TransferOperationException(409, "Insufficient available stock at the source location.");
            var destination = inventories.SingleOrDefault(x => x.ItemId == item.ItemId && x.LocationId == transfer.ToLocationId);
            if (destination is not null && (long)destination.QuantityOnHand + item.Amount > int.MaxValue)
                throw new TransferOperationException(409, "Destination quantity exceeds the supported range.");
            source.QuantityOnHand -= item.Amount;
            source.UpdatedAt = now;
            if (destination is null)
            {
                destination = new Inventory { ItemId = item.ItemId, LocationId = transfer.ToLocationId,
                    QuantityOnHand = item.Amount, QuantityExpected = 0, QuantityOrdered = 0, QuantityAllocated = 0,
                    CreatedAt = now, UpdatedAt = now };
                db.Inventories.Add(destination);
                inventories.Add(destination);
            }
            else
            {
                destination.QuantityOnHand += item.Amount;
                destination.UpdatedAt = now;
            }
        }
        transfer.TransferStatus = "Processed";
        transfer.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        logger.LogInformation("Processed batch transfer with id:{TransferId}", transfer.Id);
    }

    private Task<Transfer?> LockTransferAsync(int id, CancellationToken ct)
        => db.Transfers.FromSqlInterpolated($"SELECT * FROM public.transfer WHERE id = {id} FOR UPDATE").SingleOrDefaultAsync(ct);

    private static void EnsureScheduled(Transfer transfer)
    {
        if (transfer.TransferStatus != "Scheduled") throw new TransferOperationException(409, "Processed transfers cannot be changed or removed.");
    }

    private async Task ValidateAsync(TransferWriteRequest request, IReadOnlyList<TransferLineRequest> items, CancellationToken ct)
    {
        if (request.FromLocationId == request.ToLocationId) throw new TransferOperationException(400, "Source and destination must differ.");
        var locationIds = new[] { request.FromLocationId, request.ToLocationId };
        if (await db.Locations.CountAsync(x => locationIds.Contains(x.Id), ct) != 2)
            throw new TransferOperationException(400, "Source and destination must exist.");
        if (items.Count == 0 || items.Any(x => x is null || x.ItemId <= 0 || x.Amount <= 0))
            throw new TransferOperationException(400, "Transfer needs positive item quantities.");
        var ids = items.Select(x => x.ItemId).Distinct().ToArray();
        if (ids.Length != items.Count) throw new TransferOperationException(400, "An item may occur only once per transfer.");
        if (await db.Items.CountAsync(x => ids.Contains(x.Id), ct) != ids.Length)
            throw new TransferOperationException(400, "One or more transfer items do not exist.");
    }

    private static TransferResponse WithItems(Transfer transfer, IEnumerable<TransferItem> items)
        => new(transfer.Id, transfer.Reference, transfer.FromLocationId, transfer.ToLocationId,
            transfer.TransferStatus, transfer.CreatedAt, transfer.UpdatedAt,
            items.Select(x => new TransferLineResponse(x.ItemId, x.Amount)).ToList());
}
