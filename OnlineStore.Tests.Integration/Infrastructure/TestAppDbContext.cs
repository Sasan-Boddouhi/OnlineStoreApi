using Application.Entities;
using DataLayer.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace OnlineStore.Tests.Integration.Infrastructure;

public class TestAppDbContext : AppDbContext
{
    public TestAppDbContext(
        DbContextOptions<AppDbContext> options,
        IEnumerable<ISaveChangesInterceptor> interceptors)
        : base(options, interceptors)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // غیرفعال‌سازی تولید خودکار RowVersion برای SQLite
        modelBuilder.Entity<UserSession>()
            .Property(s => s.RowVersion)
            .ValueGeneratedNever();

        modelBuilder.Entity<RefreshTokenEntity>()
            .Property(r => r.RowVersion)
            .ValueGeneratedNever();

        // Phase 0: Inventory هم RowVersion دارد و SQLite آن را تولید نمی‌کند.
        modelBuilder.Entity<Inventory>()
            .Property(i => i.RowVersion)
            .ValueGeneratedNever();

        // M7.5: Order و SellerProduct هم RowVersion دارند و SQLite آن را تولید نمی‌کند.
        modelBuilder.Entity<Order>()
            .Property(o => o.RowVersion)
            .ValueGeneratedNever();

        modelBuilder.Entity<SellerProduct>()
            .Property(sp => sp.RowVersion)
            .ValueGeneratedNever();
    }
}