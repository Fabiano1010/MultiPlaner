
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Prototype.Data;
using Prototype.Models;
using Prototype.Services;

namespace Prototype.Controllers;

[ApiController]
[Route("api/[controller]")]
public class EventController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly EventService _eventService;

    public EventController(AppDbContext db, EventService eventService)
    {
        _db = db;
        _eventService = eventService;
    }

    [HttpGet]
    public async Task<IActionResult> GetEvents([FromQuery] int? year, [FromQuery] int? month)
    {
        var query = _db.Events.AsQueryable();

        if (year.HasValue && month.HasValue)
        {
            query = query.Where(e => 
                e.StartDate.Year == year && 
                e.StartDate.Month == month);
        }

        var events = await query.ToListAsync();
        return Ok(events);
    }

    [HttpPost]
    public async Task<IActionResult> AddEvent([FromBody] Event newEvent)
    {
        try
        {
            var result = await _eventService.AddEventAsync(newEvent);
            return CreatedAtAction(nameof(GetEvents), new { id = result.Id }, result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteEvent(int id)
    {
        var deleted = await _db.Events
            .Where(e => e.Id == id)
            .ExecuteDeleteAsync();
        if(deleted == 0)
        {
            return NotFound();
        }
        return NoContent();
    }
}

