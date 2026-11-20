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
            t.HasCheckConstraint("CK_CalendarEvent_Color",
                "[Color] IS NULL OR (LEN([Color]) = 6 AND [Color] NOT LIKE '%[^0-9A-Fa-f]%')");
        });
        b.HasAlternateKey(x => new { x.Id, x.RoomId });
        b.Property(x => x.Title).HasMaxLength(128).IsRequired();
        b.Property(x => x.Description).HasMaxLength(4000);
        b.Property(x => x.Color).HasMaxLength(6).IsUnicode(false);
        b.Property(x => x.TimeZoneId).HasMaxLength(128);
        b.HasOne<RoomEventType>().WithMany().HasForeignKey(x => new { x.EventTypeId, x.RoomId })
            .HasPrincipalKey(x => new { x.Id, x.RoomId }).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<Room>().WithMany().HasForeignKey(x => x.RoomId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<RoomMember>().WithMany().HasForeignKey(x => new { x.AuthorMemberId, x.RoomId })
            .HasPrincipalKey(x => new { x.Id, x.RoomId }).OnDelete(DeleteBehavior.NoAction);
        b.HasIndex(x => new { x.RoomId, x.StartsAtUtc, x.EndsAtUtc });
        b.HasIndex(x => new { x.RoomId, x.EventTypeId });
        b.HasIndex(x => new { x.RoomId, x.IsPinned });
        b.HasIndex(x => new { x.RoomId, x.AuthorMemberId, x.StartsAtUtc });
        b.Property(x => x.RecurrenceInterval).HasDefaultValue(1);
        b.ToTable(t => t.HasCheckConstraint("CK_Event_Recurrence", "[RecurrenceInterval] BETWEEN 1 AND 12"));
        b.HasIndex(x => x.RoomId).HasFilter("[Recurrence] <> 0");
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
        b.ToTable("AvailabilityIntervals",
            t => t.HasCheckConstraint("CK_Availability_Interval", "[EndsAtUtc] > [StartsAtUtc]"));
        b.HasOne<RoomMember>().WithMany().HasForeignKey(x => new { x.MemberId, x.RoomId })
            .HasPrincipalKey(x => new { x.Id, x.RoomId }).OnDelete(DeleteBehavior.NoAction);
        b.HasIndex(x => new { x.RoomId, x.MemberId, x.StartsAtUtc, x.EndsAtUtc });
    }
}
public sealed class EventOccurrenceExceptionConfiguration : IEntityTypeConfiguration<EventOccurrenceException>
{
    public void Configure(EntityTypeBuilder<EventOccurrenceException> b)
    {
        b.ToTable("EventOccurrenceExceptions");
        b.HasKey(x => new { x.EventId, x.OccurrenceDate });
        b.HasOne<CalendarEvent>().WithMany().HasForeignKey(x => x.EventId).OnDelete(DeleteBehavior.Cascade);
    }
}