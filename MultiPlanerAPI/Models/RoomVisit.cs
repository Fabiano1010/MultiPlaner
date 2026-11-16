namespace MultiPlanerAPI.Models;

public sealed class RoomVisit
{
    public int MemberId { get; set; }
    public DateTimeOffset LastOpenedAtUtc { get; set; }
}