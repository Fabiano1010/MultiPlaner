using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MultiPlanerAPI.Models;

namespace MultiPlanerAPI.Data.Configurations;

public sealed class RoomConfiguration : IEntityTypeConfiguration<Room>
{
    public void Configure(EntityTypeBuilder<Room> b)
    {
        b.ToTable("Rooms", t => t.HasCheckConstraint("CK_Room_Expiry", "[ExpiresAtUtc] > [CreatedAtUtc]"));
        b.Property(x => x.Name).HasMaxLength(128).IsRequired();
        b.Property(x => x.TimeZoneId).HasMaxLength(128).IsRequired();
        b.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.OwnerUserId).OnDelete(DeleteBehavior.NoAction);
        b.HasIndex(x => new { x.ExpiresAtUtc, x.Id }).HasFilter("[ArchivedAtUtc] IS NULL");
    }
}

public sealed class RoomMemberConfiguration : IEntityTypeConfiguration<RoomMember>
{
    public void Configure(EntityTypeBuilder<RoomMember> b)
    {
        b.ToTable("RoomMembers", t =>
        {
            t.HasCheckConstraint("CK_RoomMember_Identity",
                "([UserId] IS NOT NULL AND [GuestSessionId] IS NULL) OR ([UserId] IS NULL AND [GuestSessionId] IS NOT NULL)");
            t.HasCheckConstraint("CK_RoomMember_Color", "LEN([Color]) = 6 AND [Color] NOT LIKE '%[^0-9A-Fa-f]%'");
        });
        b.HasAlternateKey(x => new { x.Id, x.RoomId });
        b.Property(x => x.DisplayName).HasMaxLength(64).IsRequired();
        b.Property(x => x.Color).HasMaxLength(6).IsUnicode(false).IsRequired();
        b.HasOne<Room>().WithMany().HasForeignKey(x => x.RoomId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<GuestSession>().WithMany().HasForeignKey(x => x.GuestSessionId).OnDelete(DeleteBehavior.NoAction);
        b.HasIndex(x => new { x.RoomId, x.UserId }).IsUnique().HasFilter("[UserId] IS NOT NULL");
        b.HasIndex(x => new { x.RoomId, x.GuestSessionId }).IsUnique().HasFilter("[GuestSessionId] IS NOT NULL");
    }
}

public sealed class RoomInvitationConfiguration : IEntityTypeConfiguration<RoomInvitation>
{
    public void Configure(EntityTypeBuilder<RoomInvitation> b)
    {
        b.ToTable("RoomInvitations", t =>
        {
            t.HasCheckConstraint("CK_RoomInvitation_Expiry", "[ExpiresAtUtc] > [CreatedAtUtc]");
            t.HasCheckConstraint("CK_RoomInvitation_Uses", "[UseCount] >= 0 AND ([MaxUses] IS NULL OR ([MaxUses] > 0 AND [UseCount] <= [MaxUses]))");
        });
        b.Property(x => x.TokenHash).HasMaxLength(64).IsUnicode(false).IsRequired();
        b.HasIndex(x => x.TokenHash).IsUnique();
        b.HasOne<Room>().WithMany().HasForeignKey(x => x.RoomId).OnDelete(DeleteBehavior.NoAction);
    }
}
