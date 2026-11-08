namespace MultiPlanerAPI.Models;

public sealed class Poll : TrackedEntity
{
    public int Id { get; set; }
    public int RoomId { get; set; }
    public int AuthorMemberId { get; set; }
    public string Question { get; set; } = string.Empty;
    public bool AllowMultiple { get; set; }
    public DateTimeOffset ClosesAtUtc { get; set; }
    public DateTimeOffset? ClosedAtUtc { get; set; }
    public List<PollOption> Options { get; set; } = [];

    public DateTimeOffset? GetClosedAtUtc(DateTimeOffset now)
    {
        return ClosedAtUtc ?? (ClosesAtUtc <= now ? ClosesAtUtc : null);
    }
}

