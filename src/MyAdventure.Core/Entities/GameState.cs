namespace MyAdventure.Core.Entities;

/// <summary>Persistent game state stored in SQLite.</summary>
public record GameState : EntityBase
{
    public double Cash { get; set; }
    public double LifetimeEarnings { get; set; }
    public double AngelInvestors { get; set; }
    public int PrestigeCount { get; set; }
    public string BusinessDataJson { get; set; } = "{}";
    public string ManagerDataJson { get; set; } = "{}";
    public DateTime LastPlayedAt { get; set; } = DateTime.UtcNow;
}
