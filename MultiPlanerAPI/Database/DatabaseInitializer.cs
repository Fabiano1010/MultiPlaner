using Microsoft.EntityFrameworkCore;

namespace MultiPlanerAPI.Data;

public static class DatabaseInitializer
{
    public static async Task MigrateAsync(AppDbContext db, CancellationToken cancellationToken = default)
    {
        var known = db.Database.GetMigrations().ToHashSet(StringComparer.Ordinal);
        var applied = await db.Database.GetAppliedMigrationsAsync(cancellationToken);
        if (applied.Any(migration => !known.Contains(migration)))
            throw new InvalidOperationException(
                "This database uses the retired development schema. Recreate only the development database " +
                "or configure an empty database before applying InitialApiSchema. See docs/api-foundation.md.");
        await db.Database.MigrateAsync(cancellationToken);
    }
}
