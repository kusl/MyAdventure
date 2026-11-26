using Avalonia.Data.Converters;
using Avalonia.Media;

namespace MyAdventure.Shared.Converters;

public static class GameConverters
{
    public static FuncValueConverter<string?, IBrush> HexToBrush { get; } =
        new(hex => hex is null ? Brushes.Gray : (IBrush)new SolidColorBrush(Color.Parse(hex)));

    public static FuncValueConverter<bool, double> BoolToOpacity { get; } =
        new(enabled => enabled ? 1.0 : 0.4);

    public static FuncValueConverter<double, double> PercentToFraction { get; } =
        new(percent => Math.Clamp(percent / 100.0, 0.0, 1.0));
}
