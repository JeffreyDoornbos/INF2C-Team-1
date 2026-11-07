using CargoHub.Api.Contracts;
using CargoHub.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CargoHub.Api.Controllers;

[ApiController]
[Route("api/v1/transfers")]
public sealed class TransfersController(TransferService transfers) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(List<TransferResponse>), 200)]
    public async Task<IActionResult> GetTransfers(CancellationToken ct) => Ok(await transfers.GetTransfersAsync(ct));

    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(TransferResponse), 200)]
    public async Task<IActionResult> GetTransfer(int id, CancellationToken ct)
        => new JsonResult(await transfers.GetTransferAsync(id, ct)) { StatusCode = 200 };

    [HttpGet("{id:int}/items")]
    [ProducesResponseType(typeof(List<TransferLineResponse>), 200)]
    public async Task<IActionResult> GetItems(int id, CancellationToken ct) => Ok(await transfers.GetItemsInTransferAsync(id, ct));

    [HttpPost]
    [ProducesResponseType(201)]
    [ProducesResponseType(typeof(ProblemDetails), 400)]
    [ProducesResponseType(typeof(ProblemDetails), 409)]
    public Task<IActionResult> AddTransfer([FromBody] TransferWriteRequest request, CancellationToken ct)
        => WriteAsync(() => transfers.AddTransferAsync(request, ct), 201);

    [HttpPut("{id:int}")]
    [ProducesResponseType(200)]
    public Task<IActionResult> UpdateTransfer(int id, [FromBody] TransferWriteRequest request, CancellationToken ct)
        => WriteAsync(() => transfers.UpdateTransferAsync(id, request, ct));

    [HttpPut("{id:int}/commit")]
    [ProducesResponseType(200)]
    [ProducesResponseType(typeof(ProblemDetails), 404)]
    [ProducesResponseType(typeof(ProblemDetails), 409)]
    public Task<IActionResult> CommitTransfer(int id, CancellationToken ct)
        => WriteAsync(() => transfers.CommitTransferAsync(id, ct));

    [HttpDelete("{id:int}")]
    [ProducesResponseType(200)]
    public Task<IActionResult> RemoveTransfer(int id, CancellationToken ct)
        => WriteAsync(() => transfers.RemoveTransferAsync(id, ct));

    private async Task<IActionResult> WriteAsync(Func<Task> operation, int success = 200)
    {
        try { await operation(); return StatusCode(success); }
        catch (TransferOperationException exception) { return Problem(statusCode: exception.StatusCode, title: exception.Message); }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException postgres)
        {
            return postgres.SqlState switch
            {
                PostgresErrorCodes.UniqueViolation => Problem(statusCode: 409, title: "A transfer or inventory record already exists."),
                PostgresErrorCodes.ForeignKeyViolation or PostgresErrorCodes.CheckViolation or PostgresErrorCodes.NotNullViolation
                    => Problem(statusCode: 400, title: "Transfer data violates a database rule."),
                _ => Problem(statusCode: 500, title: "Transfer could not be saved.")
            };
        }
    }
}
