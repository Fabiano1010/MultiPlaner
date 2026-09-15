using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MultiPlanerAPI.Models;

namespace MultiPlanerAPI.Data.Configurations;

public sealed class UserConfiguration : IEntityTypeConfiguration<ApplicationUser>
{
    public void Configure(EntityTypeBuilder<ApplicationUser> b)
    {
        b.Property(x => x.DisplayName).HasMaxLength(64).IsRequired();
        b.Property(x => x.CountryCode).HasMaxLength(2).IsRequired();
        b.Property(x => x.TimeZoneId).HasMaxLength(128).IsRequired();
        b.Property(x => x.AvatarUrl).HasMaxLength(2048);
        b.Property(x => x.ContactDetails).HasMaxLength(256);
        b.Property(x => x.Email).HasMaxLength(254).IsRequired();
        b.Property(x => x.NormalizedEmail).HasMaxLength(254).IsRequired();
        b.HasIndex(x => x.NormalizedEmail).IsUnique().HasDatabaseName("EmailIndex");
    }
}

public sealed class GuestSessionConfiguration : IEntityTypeConfiguration<GuestSession>
{
    public void Configure(EntityTypeBuilder<GuestSession> b)
    {
        b.ToTable("GuestSessions", t => t.HasCheckConstraint("CK_GuestSession_Expiry", "[ExpiresAtUtc] > [CreatedAtUtc]"));
        b.Property(x => x.DisplayName).HasMaxLength(64).IsRequired();
        b.HasIndex(x => x.ExpiresAtUtc);
    }
}
