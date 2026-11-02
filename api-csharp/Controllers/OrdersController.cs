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