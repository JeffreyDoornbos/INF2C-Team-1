using CargoHub.Api.Data;
using CargoHub.Api.Domain;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json.Serialization;

namespace CargoHub.Api.Controllers;

[ApiController]
[Route("api/v1/orders")]
public sealed class OrdersController(CargoHubDbContext context) : ControllerBase
{
    private readonly OrderService service = new(context);

    [HttpGet]
    public async Task<IActionResult> GetOrders(CancellationToken ct) =>
        Ok(await service.GetOrdersAsync(null, ct));

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetOrder(int id, CancellationToken ct)
    {
        var order = await service.GetOrderAsync(id, ct);
        return new JsonResult(order) { StatusCode = 200 };
    }

    [HttpGet("/api/v1/clients/{id:int}/orders")]
    public async Task<IActionResult> GetOrdersForClient(int id, CancellationToken ct) =>
        Ok(await service.GetOrdersAsync(id, ct));

    [HttpPost]
    public async Task<IActionResult> AddOrder([FromBody] OrderWriteRequest request, CancellationToken ct)
    {
        await service.AddOrderAsync(request, ct);
        return StatusCode(201);
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> UpdateOrder(int id, [FromBody] OrderWriteRequest request, CancellationToken ct)
    {
        await service.UpdateOrderAsync(id, request, ct);
        return Ok();
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> RemoveOrder(int id, CancellationToken ct)
    {
        await service.RemoveOrderAsync(id, ct);
        return Ok();
    }
}

// DTOs avoid exposing EF entities.
public sealed class OrderItemWriteRequest
{
    [JsonPropertyName("item_id")] public int ItemId { get; set; }
    [JsonPropertyName("amount")] public int Amount { get; set; }
    [JsonPropertyName("unit_price")] public decimal? UnitPrice { get; set; }
}

public sealed class OrderWriteRequest
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("client_id")] public int ClientId { get; set; }
    [JsonPropertyName("order_date")] public DateOnly OrderDate { get; set; }
    [JsonPropertyName("request_date")] public DateOnly RequestDate { get; set; }
    [JsonPropertyName("reference")] public string Reference { get; set; } = "";
    [JsonPropertyName("customer_po_number")] public string CustomerPoNumber { get; set; } = "";
    [JsonPropertyName("order_status")] public string OrderStatus { get; set; } = "";
    [JsonPropertyName("shipping_notes")] public string? ShippingNotes { get; set; }
    [JsonPropertyName("warehouse_id")] public int WarehouseId { get; set; }
    [JsonPropertyName("ship_to_client_id")] public int ShipToClientId { get; set; }
    [JsonPropertyName("bill_to_client_id")] public int BillToClientId { get; set; }
    // null: don't touch items during PUT; []: clear all items.
    [JsonPropertyName("items")] public List<OrderItemWriteRequest>? Items { get; set; }
}

public sealed class OrderItemResponse
{
    [JsonPropertyName("item_id")] public int ItemId { get; set; }
    [JsonPropertyName("amount")] public int Amount { get; set; }
}

public sealed class OrderResponse
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("client_id")] public int ClientId { get; set; }
    [JsonPropertyName("order_date")] public DateOnly OrderDate { get; set; }
    [JsonPropertyName("request_date")] public DateOnly RequestDate { get; set; }
    [JsonPropertyName("reference")] public string Reference { get; set; } = "";
    [JsonPropertyName("customer_po_number")] public string CustomerPoNumber { get; set; } = "";
    [JsonPropertyName("order_status")] public string OrderStatus { get; set; } = "";
    [JsonPropertyName("shipping_notes")] public string? ShippingNotes { get; set; }
    [JsonPropertyName("warehouse_id")] public int WarehouseId { get; set; }
    [JsonPropertyName("ship_to_client_id")] public int ShipToClientId { get; set; }
    [JsonPropertyName("bill_to_client_id")] public int BillToClientId { get; set; }
    [JsonPropertyName("created_at")] public DateTimeOffset CreatedAt { get; set; }
    [JsonPropertyName("updated_at")] public DateTimeOffset UpdatedAt { get; set; }
    [JsonPropertyName("items")] public List<OrderItemResponse> Items { get; set; } = [];
}

public sealed class OrderService(CargoHubDbContext context)
{
    public async Task<List<OrderResponse>> GetOrdersAsync(int? clientId, CancellationToken ct = default)
    {
        var query = context.Orders.AsNoTracking().AsQueryable();
        if (clientId.HasValue)
            query = query.Where(o => o.ClientId == clientId || o.ShipToClientId == clientId || o.BillToClientId == clientId);
        var orders = await query.OrderBy(o => o.Id).ToListAsync(ct);
        var ids = orders.Select(o => o.Id).ToArray();
        var rows = await context.OrderItems.AsNoTracking().Where(x => ids.Contains(x.OrderId))
            .OrderBy(x => x.Id).ToListAsync(ct);
        return orders.Select(o => ToResponse(o, rows.Where(r => r.OrderId == o.Id))).ToList();
    }

    public async Task<OrderResponse?> GetOrderAsync(int id, CancellationToken ct = default)
    {
        var order = await context.Orders.AsNoTracking().FirstOrDefaultAsync(o => o.Id == id, ct);
        if (order is null) return null;
        var rows = await context.OrderItems.AsNoTracking().Where(x => x.OrderId == id).OrderBy(x => x.Id).ToListAsync(ct);
        return ToResponse(order, rows);
    }

    public async Task<List<OrderItemResponse>> GetItemsAsync(int id, CancellationToken ct = default) =>
        await context.OrderItems.AsNoTracking().Where(x => x.OrderId == id).OrderBy(x => x.Id)
            .Select(x => new OrderItemResponse { ItemId = x.ItemId, Amount = x.Amount }).ToListAsync(ct);

    public async Task AddOrderAsync(OrderWriteRequest request, CancellationToken ct = default)
    {
        await using var tx = await context.Database.BeginTransactionAsync(ct);
        var now = DateTimeOffset.UtcNow;
        var order = new Order { Id = request.Id, CreatedAt = now, UpdatedAt = now };
        ApplyFields(order, request);
        context.Orders.Add(order);
        await context.SaveChangesAsync(ct);
        // Python add_order stores items but doesng update inventory allocations.
        await SetItemsAsync(order.Id, request.Items ?? [], ct);
        await tx.CommitAsync(ct);
    }

    public async Task UpdateOrderAsync(int id, OrderWriteRequest request, CancellationToken ct = default)
    {
        await using var tx = await context.Database.BeginTransactionAsync(ct);
        var order = await context.Orders.FirstOrDefaultAsync(o => o.Id == id, ct);
        if (order is not null)
        {
            ApplyFields(order, request);
            order.UpdatedAt = DateTimeOffset.UtcNow;
            await context.SaveChangesAsync(ct);
        }
        //Python invokes update_items_in_order even for unknown order ids.
        if (request.Items is not null)
            await ReplaceItemsCoreAsync(id, request.Items, ct);
        await tx.CommitAsync(ct);
    }

    public async Task ReplaceItemsAsync(int id, IReadOnlyList<OrderItemWriteRequest> items, CancellationToken ct = default)
    {
        await using var tx = await context.Database.BeginTransactionAsync(ct);
        await ReplaceItemsCoreAsync(id, items, ct);
        await tx.CommitAsync(ct);
    }

    private async Task ReplaceItemsCoreAsync(int id, IReadOnlyList<OrderItemWriteRequest> items, CancellationToken ct)
    {
        var current = await context.OrderItems.Where(x => x.OrderId == id).ToListAsync(ct);
        var oldAmounts = current.GroupBy(x => x.ItemId).ToDictionary(g => g.Key, g => g.Last().Amount);
        var newAmounts = items.GroupBy(x => x.ItemId).ToDictionary(g => g.Key, g => g.Last().Amount);
        foreach (var (itemId, newAmount) in newAmounts)
        {
            var delta = newAmount - oldAmounts.GetValueOrDefault(itemId);
            if (delta != 0) await ApplyAllocationDeltaAsync(itemId, delta, ct);
        }
        foreach (var (itemId, oldAmount) in oldAmounts)
            if (!newAmounts.ContainsKey(itemId))
                await ApplyAllocationDeltaAsync(itemId, -oldAmount, ct);
        await SetItemsAsync(id, items, ct);
    }

    private async Task ApplyAllocationDeltaAsync(int itemId, int delta, CancellationToken ct)
    {
        var inventory = await context.Inventories.Where(x => x.ItemId == itemId)
            .OrderByDescending(x => x.QuantityOnHand).ThenBy(x => x.LocationId)
            .FirstOrDefaultAsync(ct);
        if (inventory is null) return;
        inventory.QuantityAllocated = Math.Max(0, inventory.QuantityAllocated + delta);
        inventory.UpdatedAt = DateTimeOffset.UtcNow;
        await context.SaveChangesAsync(ct);
        // PostgreSQL CHECK quantity_allocated <= quantity_on_hand may reject Python valid overallocations.
    }

    private async Task SetItemsAsync(int orderId, IReadOnlyList<OrderItemWriteRequest> items, CancellationToken ct)
    {
        var previous = await context.OrderItems.Where(x => x.OrderId == orderId).ToListAsync(ct);
        context.OrderItems.RemoveRange(previous);
        await context.SaveChangesAsync(ct);
        foreach (var item in items)
            context.OrderItems.Add(new OrderItem
            {
                OrderId = orderId, ItemId = item.ItemId, Amount = item.Amount,
                UnitPrice = item.UnitPrice ?? 0m
            });
        await context.SaveChangesAsync(ct);
    }

    public async Task RemoveOrderAsync(int id, CancellationToken ct = default)
    {
        await using var tx = await context.Database.BeginTransactionAsync(ct);
        var items = await context.OrderItems.Where(x => x.OrderId == id).ToListAsync(ct);
        context.OrderItems.RemoveRange(items);
        await context.SaveChangesAsync(ct);
        var order = await context.Orders.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (order is not null) context.Orders.Remove(order);
        await context.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        // FK restrictions can prevent deletion when shipments refer to the order.
    }

    private static void ApplyFields(Order order, OrderWriteRequest x)
    {
        order.ClientId = x.ClientId;
        order.OrderDate = x.OrderDate;
        order.RequestDate = x.RequestDate;
        order.Reference = x.Reference;
        order.CustomerPoNumber = x.CustomerPoNumber;
        order.OrderStatus = x.OrderStatus;
        order.ShippingNotes = x.ShippingNotes;
        order.WarehouseId = x.WarehouseId;
        order.ShipToClientId = x.ShipToClientId;
        order.BillToClientId = x.BillToClientId;
    }

    private static OrderResponse ToResponse(Order o, IEnumerable<OrderItem> rows) => new()
    {
        Id = o.Id, ClientId = o.ClientId, OrderDate = o.OrderDate, RequestDate = o.RequestDate,
        Reference = o.Reference, CustomerPoNumber = o.CustomerPoNumber, OrderStatus = o.OrderStatus,
        ShippingNotes = o.ShippingNotes, WarehouseId = o.WarehouseId,
        ShipToClientId = o.ShipToClientId, BillToClientId = o.BillToClientId,
        CreatedAt = o.CreatedAt, UpdatedAt = o.UpdatedAt,
        Items = rows.Select(r => new OrderItemResponse { ItemId = r.ItemId, Amount = r.Amount }).ToList()
    };
}