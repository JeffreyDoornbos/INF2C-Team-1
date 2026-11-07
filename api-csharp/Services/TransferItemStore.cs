using CargoHub.Api.Contracts;
using CargoHub.Api.Data;
using CargoHub.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace CargoHub.Api.Services;

public sealed class TransferItemStore(CargoHubDbContext db)
{
    public Task<List<TransferItem>> GetItemsForTransferAsync(int transferId, CancellationToken ct = default)
        => db.TransferItems.Where(x => x.TransferId == transferId).OrderBy(x => x.Id).ToListAsync(ct);

    public async Task SetItemsForTransferAsync(int transferId, IReadOnlyList<TransferLineRequest> items, CancellationToken ct = default)
    {
        await RemoveItemsForTransferAsync(transferId, ct);
        db.TransferItems.AddRange(items.Select(x => new TransferItem
            { TransferId = transferId, ItemId = x.ItemId, Amount = x.Amount }));
        await db.SaveChangesAsync(ct);
    }

    public async Task RemoveItemsForTransferAsync(int transferId, CancellationToken ct = default)
    {
        db.TransferItems.RemoveRange(await GetItemsForTransferAsync(transferId, ct));
        await db.SaveChangesAsync(ct);
    }
    // PostgreSQL identity replaces the race-prone Python max(id)+1 calculation.
    // Header, lines and inventory share the caller's transaction.
}
