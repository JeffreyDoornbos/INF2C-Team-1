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