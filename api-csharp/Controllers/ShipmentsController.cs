using CargoHub.Api.Contracts;
using CargoHub.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CargoHub.Api.Controllers;

[ApiController]
[Route("api/v1/shipments")]
public sealed class ShipmentsController(ShipmentService shipments) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(List<ShipmentResponse>), 200)]
    public async Task<IActionResult> GetShipments(CancellationToken ct) => Ok(await shipments.GetShipmentsAsync(ct));

    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(ShipmentResponse), 200)]
    public async Task<IActionResult> GetShipment(int id, CancellationToken ct)
        => new JsonResult(await shipments.GetShipmentAsync(id, ct)) { StatusCode = 200 };

    [HttpGet("{id:int}/items")]
    [ProducesResponseType(typeof(List<ShipmentLineResponse>), 200)]
    public async Task<IActionResult> GetItems(int id, CancellationToken ct) => Ok(await shipments.GetItemsInShipmentAsync(id, ct));

    [HttpGet("{id:int}/orders")]
    [ProducesResponseType(typeof(int[]), 200)]
    public async Task<IActionResult> GetOrders(int id, CancellationToken ct) => Ok(await shipments.GetOrderIdsInShipmentAsync(id, ct));

    [HttpPost]
    [ProducesResponseType(201)]
    [ProducesResponseType(typeof(ProblemDetails), 400)]
    [ProducesResponseType(typeof(ProblemDetails), 409)]
    public Task<IActionResult> AddShipment([FromBody] ShipmentWriteRequest request, CancellationToken ct)
        => WriteAsync(() => shipments.AddShipmentAsync(request, ct), 201);

    [HttpPut("{id:int}")]
    public Task<IActionResult> UpdateShipment(int id, [FromBody] ShipmentWriteRequest request, CancellationToken ct)
        => WriteAsync(() => shipments.UpdateShipmentAsync(id, request, ct));

    [HttpPut("{id:int}/items")]
    public Task<IActionResult> UpdateItems(int id, [FromBody] List<ShipmentLineRequest> items, CancellationToken ct)
        => WriteAsync(() => shipments.UpdateItemsInShipmentAsync(id, items, ct));

    [HttpPut("{id:int}/orders")]
    public Task<IActionResult> UpdateOrders(int id, [FromBody] List<int> orderIds, CancellationToken ct)
        => WriteAsync(() => shipments.UpdateOrdersInShipmentAsync(id, orderIds, ct));

    [HttpDelete("{id:int}")]
    public Task<IActionResult> RemoveShipment(int id, CancellationToken ct)
        => WriteAsync(() => shipments.RemoveShipmentAsync(id, ct));

    private async Task<IActionResult> WriteAsync(Func<Task> operation, int success = 200)
    {
        try { await operation(); return StatusCode(success); }
        catch (ShipmentValidationException exception) { return Problem(statusCode: 400, title: exception.Message); }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException postgres)
        {
            return postgres.SqlState switch
            {
                PostgresErrorCodes.UniqueViolation => Problem(statusCode: 409, title: "A shipment with this ID already exists."),
                PostgresErrorCodes.ForeignKeyViolation or PostgresErrorCodes.CheckViolation or PostgresErrorCodes.NotNullViolation
                    => Problem(statusCode: 400, title: "Shipment data violates a database rule."),
                _ => Problem(statusCode: 500, title: "Shipment could not be saved.")
            };
        }
    }
}
