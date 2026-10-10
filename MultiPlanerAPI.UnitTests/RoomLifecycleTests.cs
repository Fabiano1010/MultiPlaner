using Microsoft.EntityFrameworkCore;
using MultiPlanerAPI.Data;
using MultiPlanerAPI.Infrastructure;
using MultiPlanerAPI.Models;
using MultiPlanerAPI.Modules.Rooms;

namespace MultiPlanerAPI.UnitTests;

public sealed class RoomLifecycleTests
{
    private static readonly DateTimeOffset Expiry = new(2027, 4, 10, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void AutomaticArchiveUsesChosenExpiryAndStartsExactlyAtTheBoundary()
    {
        var room = new Room { ArchiveOnExpiry = true, ExpiresAtUtc = Expiry, CreatedAtUtc = Expiry.AddMonths(-6) };
        Assert.Null(room.GetArchivedAtUtc(room.CreatedAtUtc.AddMonths(3)));
        Assert.Null(room.GetArchivedAtUtc(Expiry.AddTicks(-1)));
        Assert.Equal(Expiry, room.GetArchivedAtUtc(Expiry));
        Assert.Equal(Expiry, room.GetArchivedAtUtc(Expiry.AddDays(1)));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ExpiredRoomIsAlwaysReadOnlyButReadableOnlyWhenPreserved(bool automaticArchive)
    {
        var clock = new FixedClock(Expiry);
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().Options, clock);
        var access = new RoomAccessService(db, null!, clock);
        var room = new Room { ArchiveOnExpiry = automaticArchive, ExpiresAtUtc = Expiry };
        Assert.Equal(409, Assert.Throws<ApiException>(() => access.RequireActive(room)).StatusCode);
        if (automaticArchive) access.RequireReadable(room);
        else Assert.Equal(410, Assert.Throws<ApiException>(() => access.RequireReadable(room)).StatusCode);
    }

    [Fact]
    public void ManualArchiveDateTakesPrecedenceOverAutomaticArchive()
    {
        var archived = Expiry.AddDays(-10);
        var room = new Room { ArchiveOnExpiry = true, ExpiresAtUtc = Expiry, ArchivedAtUtc = archived };
        Assert.Equal(archived, room.GetArchivedAtUtc(Expiry.AddDays(10)));
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
