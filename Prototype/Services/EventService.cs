using Microsoft.EntityFrameworkCore;
using Prototype.Data;
using Prototype.Models;

public class EventService
{
    private readonly AppDbContext _db;

    public EventService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<Event> AddEventAsync(Event newEvent)
    {

        if (newEvent.EndDate < newEvent.StartDate)
            throw new ArgumentException("EndDate nie może być wcześniej niż StartDate");


        if (newEvent.StartDate < DateTime.Today)
            throw new ArgumentException("Nie można dodać wydarzenia w przeszłości");

      
        var overlap = await _db.Events.AnyAsync(e =>
            e.StartDate < newEvent.EndDate &&
            e.EndDate > newEvent.StartDate);

        if (overlap)
            throw new ArgumentException("Wydarzenie nakłada się na istniejące");

        _db.Events.Add(newEvent);
        await _db.SaveChangesAsync();
        return newEvent;
    }
}