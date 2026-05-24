using System.Net.Http.Json;
using MauiApp1.Models;
namespace MauiApp1.Services;

public class EventService
{
    private readonly HttpClient _http;

    public EventService(HttpClient http)
    {
        _http = http;
    }

    public async Task<List<Event>> GetEventsAsync(int year, int month)
    {
        return await _http.GetFromJsonAsync<List<Event>>(
            $"api/event?year={year}&month={month}") ?? new List<Event>();
    }

    public async Task<Event?> AddEventAsync(Event newEvent)
    {
        var response = await _http.PostAsJsonAsync("api/events", newEvent);
        
        if (response.IsSuccessStatusCode)
            return await response.Content.ReadFromJsonAsync<Event>();
            
        return null;
    }

    public async Task<bool> DeleteEventAsync(int id)
    {
        var response = await _http.DeleteAsync($"api/events/{id}");
        return response.IsSuccessStatusCode;
    }
    
    public async Task<List<Event>> GetEventsAtDayAsync(DateTime date)
    {
        return await _http.GetFromJsonAsync<List<Event>>(
                   $"api/event/day?year={date.Year}&month={date.Month}&day={date.Day}") 
               ?? new List<Event>();
    }
}