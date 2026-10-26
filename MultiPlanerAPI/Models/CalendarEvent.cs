using MultiPlanerSharedModels.Contracts.Events;

namespace MultiPlanerAPI.Models;

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
    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDateExclusive { get; set; }
    public string? TimeZoneId { get; set; }
    public EventCategory Category { get; set; }
    public string? Color { get; set; }
    public bool IsPinned { get; set; }
    public bool IsHighPriority { get; set; }
}
