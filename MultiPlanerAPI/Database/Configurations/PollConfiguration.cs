using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MultiPlanerAPI.Models;

namespace MultiPlanerAPI.Data.Configurations;

public sealed class PollConfiguration : IEntityTypeConfiguration<Poll> {
    public void Configure(EntityTypeBuilder<Poll> builder) {
        builder.ToTable("Polls", t => t.HasCheckConstraint("CK_Poll_Closes", "[ClosesAtUtc] > [CreatedAtUtc]"));
        builder.Property(x => x.Question).HasMaxLength(256).IsRequired();
        builder.HasOne<Room>().WithMany().HasForeignKey(x => x.RoomId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<RoomMember>().WithMany().HasForeignKey(x => new { x.AuthorMemberId, x.RoomId })
            .HasPrincipalKey(x => new { x.Id, x.RoomId }).OnDelete(DeleteBehavior.NoAction);
        builder.HasMany(x => x.Options).WithOne().HasForeignKey(x => x.PollId).OnDelete(DeleteBehavior.NoAction);
        builder.HasIndex(x => new { x.RoomId, x.ClosesAtUtc });
    }
}

public sealed class PollOptionConfiguration : IEntityTypeConfiguration<PollOption>
{
    public void Configure(EntityTypeBuilder<PollOption> builder)
    {
        builder.ToTable("PollOptions", t =>
            t.HasCheckConstraint("CK_PollOption_Color", "LEN([Color]) = 6 AND [Color] NOT LIKE '%[^0-9A-Fa-f]%'"));
        builder.HasAlternateKey(x => new { x.Id, x.PollId });
        builder.Property(x => x.Text).HasMaxLength(128).IsRequired();
        builder.Property(x => x.Color).HasMaxLength(6).IsUnicode(false).IsRequired();
        builder.HasIndex(x => new { x.PollId, x.Position });   // not unique on purpose: swapping options in one SaveChanges
    }
}
public sealed class PollVoteConfiguration : IEntityTypeConfiguration<PollVote>
{
    public void Configure(EntityTypeBuilder<PollVote> builder)
    {
        builder.ToTable("PollVotes");
        builder.HasKey(x => new { x.PollId, x.MemberId, x.Slot });
        builder.HasOne<PollOption>().WithMany().HasForeignKey(x => new { x.OptionId, x.PollId })
            .HasPrincipalKey(x => new { x.Id, x.PollId }).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<RoomMember>().WithMany().HasForeignKey(x => x.MemberId).OnDelete(DeleteBehavior.NoAction);
    }
}
