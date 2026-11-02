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