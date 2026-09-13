using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using MultiPlanerAPI.Models;

namespace MultiPlanerAPI.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options, TimeProvider timeProvider)
    : IdentityDbContext<ApplicationUser, IdentityRole<int>, int>(options)
{
    public DbSet<GuestSession> GuestSessions => Set<GuestSession>();
    public DbSet<Room> Rooms => Set<Room>();
    public DbSet<RoomMember> RoomMembers => Set<RoomMember>();
    public DbSet<RoomInvitation> RoomInvitations => Set<RoomInvitation>();
    public DbSet<CalendarEvent> CalendarEvents => Set<CalendarEvent>();
    public DbSet<EventParticipant> EventParticipants => Set<EventParticipant>();
    public DbSet<EventAttachment> EventAttachments => Set<EventAttachment>();
    public DbSet<ChatMessage> ChatMessages => Set<ChatMessage>();
    public DbSet<AvailabilityInterval> AvailabilityIntervals => Set<AvailabilityInterval>();
    public DbSet<RoomActivity> RoomActivities => Set<RoomActivity>();
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.HasDefaultSchema("dbo");
        builder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        foreach (var entity in builder.Model.GetEntityTypes()
                     .Where(e => typeof(TrackedEntity).IsAssignableFrom(e.ClrType)))
            builder.Entity(entity.ClrType).Property(nameof(TrackedEntity.RowVersion)).IsRowVersion();
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        SetTimestamps();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        SetTimestamps();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void SetTimestamps()
    {
        var now = timeProvider.GetUtcNow();
        foreach (var entry in ChangeTracker.Entries<ITimestamped>())
        {
            if (entry.State == EntityState.Added)
                entry.Entity.CreatedAtUtc = entry.Entity.UpdatedAtUtc = now;
            else if (entry.State == EntityState.Modified)
            {
                entry.Property(nameof(ITimestamped.CreatedAtUtc)).IsModified = false;
                entry.Entity.UpdatedAtUtc = now;
            }
        }
    }
}
