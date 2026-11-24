using MultiPlanerAPI.Models;

namespace MultiPlanerAPI.Modules.Events;

/// <summary>
/// Projection row combining an event with its room, type, author, and viewing member id.
/// </summary>
internal sealed class EventRow
{
    public required CalendarEvent Event { get; init; }
    public required RoomEventType Type { get; init; }
    public required Room Room { get; init; }
    public required RoomMember Author { get; init; }
    public required int ViewerMemberId { get; init; }
}
