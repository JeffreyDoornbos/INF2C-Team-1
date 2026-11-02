using CargoHub.Api.Data;
using Microsoft.AspNetCore.Mvc;

namespace CargoHub.Api.Controllers;

// Endpoints for order lines
[ApiController]
[Route("api/v1/orders/{id:int}/items")]
public sealed class OrderItemsController(CargoHubDbContext context) : ControllerBase
{
    private readonly OrderService service = new(context);

    [HttpGet]
    public async Task<IActionResult> GetItems(int id, CancellationToken ct) =>
        Ok(await service.GetItemsAsync(id, ct));

    [HttpPut]
    public async Task<IActionResult> ReplaceItems(int id, [FromBody] List<OrderItemWriteRequest> items, CancellationToken ct)
    {
        await service.ReplaceItemsAsync(id, items, ct);
        return Ok();
    }
}
