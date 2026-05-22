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
            // All numeric progression fields are stored as TEXT under
            // BigDouble's canonical string format ("1.5e200" etc.). The
            // GameEngine round-trips them through BigDouble.Parse /
            // BigDouble.ToCanonicalString — see GameState's XML docs.
            e.Property(g => g.CashText).HasDefaultValue("0");
            e.Property(g => g.LifetimeEarningsText).HasDefaultValue("0");
            e.Property(g => g.AngelInvestorsText).HasDefaultValue("0");
            e.Property(g => g.PrestigeCount).HasDefaultValue(0);
            e.Property(g => g.BusinessDataJson).HasDefaultValue("{}");
            e.Property(g => g.ManagerDataJson).HasDefaultValue("{}");
        });
    }
}
