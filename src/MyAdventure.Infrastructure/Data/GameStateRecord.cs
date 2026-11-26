namespace MyAdventure.Infrastructure.Data;

public sealed class GameStateRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string CashText { get; set; } = "0";
    public string LifetimeEarningsText { get; set; } = "0";
    public string AngelInvestorsText { get; set; } = "0";
    public int PrestigeCount { get; set; }
    public string BusinessDataJson { get; set; } = "{}";
    public string ManagerDataJson { get; set; } = "{}";
    public DateTime LastPlayedAt { get; set; }
}
