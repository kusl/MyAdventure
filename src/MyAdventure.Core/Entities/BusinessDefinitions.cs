namespace MyAdventure.Core.Entities;

public static class BusinessDefinitions
{
    public static IReadOnlyList<Business> CreateDefaults() =>
    [
        new()
        {
            Id = "lemonade", Name = "Lemonade Stand", Icon = "🍋",
            Color = "#FFD700", BaseCost = 4, BaseRevenue = 1,
            BaseTimeSeconds = 0.6, CostMultiplier = 1.07
        },
        new()
        {
            Id = "newspaper", Name = "Newspaper Route", Icon = "📰",
            Color = "#4FC3F7", BaseCost = 60, BaseRevenue = 60,
            BaseTimeSeconds = 3.0, CostMultiplier = 1.15
        },
        new()
        {
            Id = "carwash", Name = "Car Wash", Icon = "🚗",
            Color = "#81C784", BaseCost = 720, BaseRevenue = 540,
            BaseTimeSeconds = 6.0, CostMultiplier = 1.14
        },
        new()
        {
            Id = "pizza", Name = "Pizza Delivery", Icon = "🍕",
            Color = "#FF7043", BaseCost = 8_640, BaseRevenue = 4_320,
            BaseTimeSeconds = 12.0, CostMultiplier = 1.13
        },
        new()
        {
            Id = "donut", Name = "Donut Shop", Icon = "🍩",
            Color = "#CE93D8", BaseCost = 103_680, BaseRevenue = 51_840,
            BaseTimeSeconds = 24.0, CostMultiplier = 1.12
        },
        new()
        {
            Id = "shrimp", Name = "Shrimp Boat", Icon = "🦐",
            Color = "#F48FB1", BaseCost = 1_244_160, BaseRevenue = 622_080,
            BaseTimeSeconds = 96.0, CostMultiplier = 1.11
        },
    ];
}
