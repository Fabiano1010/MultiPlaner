using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MultiPlanerAPI.Models;

namespace MultiPlanerAPI.Data.Configurations;

public sealed class ChatMessageConfiguration : IEntityTypeConfiguration<ChatMessage>
{
    public void Configure(EntityTypeBuilder<ChatMessage> b)
    {
        b.Property(x => x.Text).HasMaxLength(2000).IsRequired();
        b.HasOne<RoomMember>().WithMany().HasForeignKey(x => new { x.AuthorMemberId, x.RoomId })
            .HasPrincipalKey(x => new { x.Id, x.RoomId }).OnDelete(DeleteBehavior.NoAction);
        b.HasIndex(x => new { x.RoomId, x.Id });
        b.HasIndex(x => new { x.RoomId, x.AuthorMemberId, x.ClientRequestId }).IsUnique();
    }
}

public sealed class RoomActivityConfiguration : IEntityTypeConfiguration<RoomActivity>
{
    public void Configure(EntityTypeBuilder<RoomActivity> b)
    {
        b.Property(x => x.Kind).HasMaxLength(64).IsRequired();
        b.Property(x => x.ResourceId).HasMaxLength(64).IsRequired();
        b.HasOne<Room>().WithMany().HasForeignKey(x => x.RoomId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<RoomMember>().WithMany().HasForeignKey(x => new { x.ActorMemberId, x.RoomId })
            .HasPrincipalKey(x => new { x.Id, x.RoomId }).OnDelete(DeleteBehavior.NoAction);
        b.HasIndex(x => new { x.RoomId, x.Id });
    }
}

public sealed class AuditEntryConfiguration : IEntityTypeConfiguration<AuditEntry>
{
    public void Configure(EntityTypeBuilder<AuditEntry> b)
    {
        b.Property(x => x.Operation).HasMaxLength(64).IsRequired();
        b.Property(x => x.ResourceType).HasMaxLength(64).IsRequired();
        b.Property(x => x.ResourceId).HasMaxLength(64);
        b.Property(x => x.TraceId).HasMaxLength(128).IsRequired();
        b.HasIndex(x => x.OccurredAtUtc);
        b.HasIndex(x => x.UserId);
    }
}

public sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> b)
    {
        b.Property(x => x.Kind).HasMaxLength(64).IsRequired();
        b.Property(x => x.PayloadJson).IsRequired();
        b.Property(x => x.RowVersion).IsRowVersion();
        b.HasIndex(x => new { x.CreatedAtUtc, x.LockedUntilUtc }).HasFilter("[ProcessedAtUtc] IS NULL");
    }
}
