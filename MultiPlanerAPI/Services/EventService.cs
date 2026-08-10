using Microsoft.EntityFrameworkCore;
using MultiPlanerAPI.Data;
using MultiPlanerAPI.Models;

namespace MultiPlanerAPI.Services;

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
            throw new ArgumentException("EndDate cant be before StartDate");


        if (newEvent.StartDate < DateTime.Today)
            throw new ArgumentException("Cannot add event in the past");

        _db.Events.Add(newEvent);
        await _db.SaveChangesAsync();
        return newEvent;
    }
}