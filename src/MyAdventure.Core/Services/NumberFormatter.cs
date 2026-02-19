namespace MyAdventure.Core.Services;

/// <summary>
/// Formats large numbers into readable abbreviated strings.
/// e.g. 1234 -> "1.23K", 1234567 -> "1.23M"
/// </summary>
public static class NumberFormatter
{
    private static readonly (double threshold, string suffix)[] Suffixes =
    [
        (1e33, "D"), (1e30, "N"), (1e27, "O"), (1e24, "Sp"),
        (1e21, "Sx"), (1e18, "Qi"), (1e15, "Qa"), (1e12, "T"),
        (1e9, "B"), (1e6, "M"), (1e3, "K")
    ];

    public static string Format(double value)
    {
        if (value < 0) return $"-{Format(-value)}";
        if (value < 1000) return value.ToString("F2");

        foreach (var (threshold, suffix) in Suffixes)
        {
            if (value >= threshold)
                return $"{value / threshold:F2} {suffix}";
        }

        return value.ToString("F2");
    }
}
