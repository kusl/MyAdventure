using Microsoft.EntityFrameworkCore;
using MyAdventure.Core.Entities;

namespace MyAdventure.Infrastructure.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<GameState> GameStates => Set<GameState>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<GameState>(e =>
        {
            e.HasKey(g => g.Id);
            e.Property(g => g.Cash).HasDefaultValue(0);
            e.Property(g => g.LifetimeEarnings).HasDefaultValue(0);
            e.Property(g => g.AngelInvestors).HasDefaultValue(0);
            e.Property(g => g.PrestigeCount).HasDefaultValue(0);
            e.Property(g => g.BusinessDataJson).HasDefaultValue("{}");
            e.Property(g => g.ManagerDataJson).HasDefaultValue("{}");
        });
    }
}
