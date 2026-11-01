using CargoHub.Api.Data;
using CargoHub.Api.Domain;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CargoHub.Api.Controllers;

// Base route for location endpoints, matching /api/v1/locations
[ApiController]
[Route("api/v1/locations")]
public class LocationsController : ControllerBase
{
    private readonly CargoHubDbContext _context;

    // Database context is injected via dependency injection
    public LocationsController(CargoHubDbContext context)
    {
        _context = context;
    }

    // GET /api/v1/locations - Retrieve all locations
    [HttpGet]
    public async Task<ActionResult<IEnumerable<Location>>> GetLocations()
    {
        var locations = await _context.Locations.AsNoTracking().ToListAsync();
        return Ok(locations);
    }

    // GET /api/v1/locations/{id} - Retrieve a single location by ID
    [HttpGet("{id:int}")]
    public async Task<ActionResult<Location>> GetLocation(int id)
    {
        var location = await _context.Locations.FindAsync(id);
        if (location == null)
        {
            return NotFound();
        }

        return Ok(location);
    }

    // POST /api/v1/locations - Create a new location
    [HttpPost]
    public async Task<ActionResult<Location>> AddLocation([FromBody] Location location)
    {
        // Set creation and modification timestamps
        location.CreatedAt = DateTimeOffset.UtcNow;
        location.UpdatedAt = DateTimeOffset.UtcNow;

        _context.Locations.Add(location);
        await _context.SaveChangesAsync();

        // Return HTTP 201 Created to match Python route behavior
        return StatusCode(201, location);
    }

    // PUT /api/v1/locations/{id} - Update an existing location by ID
    [HttpPut("{id:int}")]
    public async Task<IActionResult> UpdateLocation(int id, [FromBody] Location updatedLocation)
    {
        var existingLocation = await _context.Locations.FindAsync(id);
        if (existingLocation == null)
        {
            return NotFound();
        }

        // Apply updated field values
        existingLocation.WarehouseId = updatedLocation.WarehouseId;
        existingLocation.Code = updatedLocation.Code;
        existingLocation.Name = updatedLocation.Name;
        existingLocation.UpdatedAt = DateTimeOffset.UtcNow;

        await _context.SaveChangesAsync();

        return Ok(existingLocation);
    }

    // DELETE /api/v1/locations/{id} - Remove a location by ID
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> DeleteLocation(int id)
    {
        var location = await _context.Locations.FindAsync(id);
        if (location == null)
        {
            return NotFound();
        }

        _context.Locations.Remove(location);
        await _context.SaveChangesAsync();

        return Ok();
    }
}