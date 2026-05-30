using Application.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace OnlineStore.Tests.Integration.Infrastructure;

public sealed class SqliteRowVersionFixInterceptor : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        FixRowVersion(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        FixRowVersion(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private static void FixRowVersion(DbContext? context)
    {
        if (context is null) return;

        foreach (var entry in context.ChangeTracker.Entries()
                     .Where(e => e.State == EntityState.Added))
        {
            if (entry.Entity is UserSession session &&
                (session.RowVersion is null || session.RowVersion.Length == 0))
                session.RowVersion = new byte[8];

            if (entry.Entity is RefreshTokenEntity token &&
                (token.RowVersion is null || token.RowVersion.Length == 0))
                token.RowVersion = new byte[8];
        }
    }
}