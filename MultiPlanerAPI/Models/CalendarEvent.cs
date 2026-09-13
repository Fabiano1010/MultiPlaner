namespace MultiPlanerAPI.Models;

public enum EventCategory { Meeting, Training, Social, BusinessTrip, Leave, Shift }

public sealed class CalendarEvent : TrackedEntity
{
    public int Id { get; set; }
    public int RoomId { get; set; }
    public int AuthorMemberId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateTimeOffset StartsAtUtc { get; set; }
    public DateTimeOffset EndsAtUtc { get; set; }
    public bool IsAllDay { get; set; }
    public EventCategory Category { get; set; }
    public string? Color { get; set; }
    public bool IsPinned { get; set; }
}
