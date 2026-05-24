using Microsoft.EntityFrameworkCore;
using Prototype.Data;
using Prototype.Models;

namespace Prototype.Services;

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

        _db.Events.Add(newEvent);
        await _db.SaveChangesAsync();
        return newEvent;
    }
}