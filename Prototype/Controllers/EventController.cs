
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
    [HttpGet]
    public async Task<IActionResult> GetEvents([FromQuery] int? year, [FromQuery] int? month)
    {
        var query = _db.Events.AsQueryable();

        if (year.HasValue && month.HasValue)
        {
            var firstDay = new DateTime(year.Value, month.Value, 1);
            var lastDay = firstDay.AddMonths(1).AddDays(-1);

            query = query.Where(e =>
                e.StartDate.Date <= lastDay &&
                e.EndDate.Date >= firstDay);
        }

        var events = await query.ToListAsync();
        return Ok(events);
    }

 [HttpGet("day")]
     public async Task<IActionResult> GetEventsAtDay([FromQuery] int year, [FromQuery] int month, [FromQuery] int day)
     {
         var date = new DateTime(year, month, day);
        
         var events = await _db.Events
             .Where(e => e.StartDate.Date <= date && e.EndDate.Date >= date)
             .ToListAsync();

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

