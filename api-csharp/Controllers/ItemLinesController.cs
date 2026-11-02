using CargoHub.Api.Data;
using CargoHub.Api.Domain;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CargoHub.Api.Controllers;

[ApiController]
[Route("api/v1/item_lines")]
public class ItemLinesController : ControllerBase
{
    private readonly CargoHubDbContext _context;

    public ItemLinesController(CargoHubDbContext context)
    {
        _context = context;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<ItemLine>>> GetItemLines()
    {
        var records = await _context.ItemLines
            .AsNoTracking()
            .OrderBy(record => record.Id)
            .ToListAsync();

        return Ok(records);
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetItemLine(int id)
    {
        var record = await _context.ItemLines
            .AsNoTracking()
            .SingleOrDefaultAsync(record => record.Id == id);

        // JsonResult preserves Python's 200 + null instead of MVC's automatic 204.
        return new JsonResult(record) { StatusCode = StatusCodes.Status200OK };
    }

    [HttpPost]
    public async Task<IActionResult> AddItemLine([FromBody] ItemLine record)
    {
        var now = DateTimeOffset.UtcNow;
        record.CreatedAt = now;
        record.UpdatedAt = now;

        _context.ItemLines.Add(record);
        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateException exception) when (
            exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return Problem(statusCode: StatusCodes.Status409Conflict,
                title: "A item line with this ID already exists.");
        }

        // The legacy POST response has no body.
        return StatusCode(StatusCodes.Status201Created);
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> UpdateItemLine(int id, [FromBody] ItemLine updated)
    {
        var existing = await _context.ItemLines.FindAsync(id);
        if (existing is null)
        {
            return Ok();
        }

        // The route identifies the record; preserve its ID and creation timestamp.
        existing.Name = updated.Name;
        existing.Description = updated.Description;
        existing.UpdatedAt = DateTimeOffset.UtcNow;
        await _context.SaveChangesAsync();

        return Ok();
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> DeleteItemLine(int id)
    {
        var existing = await _context.ItemLines.FindAsync(id);
        if (existing is null)
        {
            return Ok();
        }

        _context.ItemLines.Remove(existing);
        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateException exception) when (
            exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.ForeignKeyViolation })
        {
            return Problem(statusCode: StatusCodes.Status409Conflict,
                title: "This item line is still referenced by an item.");
        }

        return Ok();
    }
}
