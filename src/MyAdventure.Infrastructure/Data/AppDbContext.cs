using Microsoft.EntityFrameworkCore;

namespace MyAdventure.Infrastructure.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<GameStateRecord> GameStates => Set<GameStateRecord>();

    public DbSet<PlayerSettingsRecord> PlayerSettings => Set<PlayerSettingsRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<GameStateRecord>(e =>
        {
            e.ToTable("GameStates");
            e.HasKey(g => g.Id);
        });

        modelBuilder.Entity<PlayerSettingsRecord>(e =>
        {
            e.ToTable("PlayerSettings");
            e.HasKey(s => s.Id);
            e.Property(s => s.Id).ValueGeneratedNever();
        });
    }
}
