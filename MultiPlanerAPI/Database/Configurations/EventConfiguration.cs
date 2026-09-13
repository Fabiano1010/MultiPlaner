using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MultiPlanerAPI.Models;

namespace MultiPlanerAPI.Data.Configurations;

public sealed class EventConfiguration : IEntityTypeConfiguration<CalendarEvent>
{
    public void Configure(EntityTypeBuilder<CalendarEvent> b)
    {
        b.ToTable("CalendarEvents", t =>
        {
            t.HasCheckConstraint("CK_CalendarEvent_Interval", "[EndsAtUtc] > [StartsAtUtc]");
            t.HasCheckConstraint("CK_CalendarEvent_Category", "[Category] IN ('Meeting','Training','Social','BusinessTrip','Leave','Shift')");
            t.HasCheckConstraint("CK_CalendarEvent_Color", "[Color] IS NULL OR (LEN([Color]) = 6 AND [Color] NOT LIKE '%[^0-9A-Fa-f]%')");
        });
        b.HasAlternateKey(x => new { x.Id, x.RoomId });
        b.Property(x => x.Title).HasMaxLength(128).IsRequired();
        b.Property(x => x.Description).HasMaxLength(4000);
        b.Property(x => x.Color).HasMaxLength(6).IsUnicode(false);
        b.Property(x => x.Category).HasConversion<string>().HasMaxLength(32);
        b.HasOne<Room>().WithMany().HasForeignKey(x => x.RoomId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<RoomMember>().WithMany().HasForeignKey(x => new { x.AuthorMemberId, x.RoomId })
            .HasPrincipalKey(x => new { x.Id, x.RoomId }).OnDelete(DeleteBehavior.NoAction);
        b.HasIndex(x => new { x.RoomId, x.StartsAtUtc, x.EndsAtUtc });
        b.HasIndex(x => new { x.RoomId, x.Category });
        b.HasIndex(x => new { x.RoomId, x.IsPinned });
    }
}

public sealed class EventParticipantConfiguration : IEntityTypeConfiguration<EventParticipant>
{
    public void Configure(EntityTypeBuilder<EventParticipant> b)
    {
        b.HasKey(x => new { x.EventId, x.MemberId });
        b.HasOne<CalendarEvent>().WithMany().HasForeignKey(x => new { x.EventId, x.RoomId })
            .HasPrincipalKey(x => new { x.Id, x.RoomId }).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<RoomMember>().WithMany().HasForeignKey(x => new { x.MemberId, x.RoomId })
            .HasPrincipalKey(x => new { x.Id, x.RoomId }).OnDelete(DeleteBehavior.NoAction);
    }
}

public sealed class EventAttachmentConfiguration : IEntityTypeConfiguration<EventAttachment>
{
    public void Configure(EntityTypeBuilder<EventAttachment> b)
    {
        b.ToTable("EventAttachments", t => t.HasCheckConstraint("CK_EventAttachment_Size", "[SizeBytes] > 0"));
        b.Property(x => x.StorageKey).HasMaxLength(512).IsRequired();
        b.Property(x => x.OriginalFileName).HasMaxLength(255).IsRequired();
        b.Property(x => x.ContentType).HasMaxLength(128).IsRequired();
        b.HasIndex(x => x.StorageKey).IsUnique();
        b.HasOne<CalendarEvent>().WithMany().HasForeignKey(x => x.EventId).OnDelete(DeleteBehavior.NoAction);
    }
}

public sealed class AvailabilityConfiguration : IEntityTypeConfiguration<AvailabilityInterval>
{
    public void Configure(EntityTypeBuilder<AvailabilityInterval> b)
    {
        b.ToTable("AvailabilityIntervals", t => t.HasCheckConstraint("CK_Availability_Interval", "[EndsAtUtc] > [StartsAtUtc]"));
        b.HasOne<RoomMember>().WithMany().HasForeignKey(x => new { x.MemberId, x.RoomId })
            .HasPrincipalKey(x => new { x.Id, x.RoomId }).OnDelete(DeleteBehavior.NoAction);
        b.HasIndex(x => new { x.RoomId, x.MemberId, x.StartsAtUtc, x.EndsAtUtc });
    }
}
