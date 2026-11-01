using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MultiPlanerAPI.Models;
namespace MultiPlanerAPI.Data.Configurations;
public sealed class UserThemeConfiguration : IEntityTypeConfiguration<UserTheme>
{
    public void Configure(EntityTypeBuilder<UserTheme> b)
    {
        b.HasKey(x => x.UserId);
        b.HasOne<ApplicationUser>().WithOne().HasForeignKey<UserTheme>(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        b.OwnsOne(x => x.Palette, owned => {
            foreach (var p in typeof(MultiPlanerSharedModels.Contracts.Users.ThemePalette).GetProperties())
                owned.Property(p.Name).HasMaxLength(7).IsRequired();
        });
    }
}
public sealed class RoomEventTypeConfiguration : IEntityTypeConfiguration<RoomEventType>
{
    public void Configure(EntityTypeBuilder<RoomEventType> b)
    {
        b.HasKey(x => x.Id);
        b.HasAlternateKey(x => new { x.Id, x.RoomId });
        b.Property(x => x.Name).HasMaxLength(64).IsRequired();
        b.Property(x => x.NormalizedName).HasMaxLength(64).IsRequired();
        b.Property(x => x.Color).HasMaxLength(6).IsRequired();
        b.HasIndex(x => new { x.RoomId, x.NormalizedName }).IsUnique();
        b.HasOne<Room>().WithMany().HasForeignKey(x => x.RoomId).OnDelete(DeleteBehavior.NoAction);
    }
}
