using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace MyAdventure.Shared.Converters;

public class HexToBrushConverter : IValueConverter
{
    public static readonly HexToBrushConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is string hex ? new SolidColorBrush(Color.Parse(hex)) : Brushes.Gray;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public class BoolToOpacityConverter : IValueConverter
{
    public static readonly BoolToOpacityConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? 1.0 : 0.4;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Converts a width threshold check: returns true if the bound width >= parameter.
/// Use in adaptive panels to show/hide detail info based on available space.
/// </summary>
public class WidthThresholdConverter : IValueConverter
{
    public static readonly WidthThresholdConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is double width && parameter is string thresholdStr
            && double.TryParse(thresholdStr, CultureInfo.InvariantCulture, out var threshold))
        {
            return width >= threshold;
        }
        return true; // default to showing
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Converts a percentage (0-100) to a fraction (0.0-1.0) for ScaleTransform.
/// </summary>
public class PercentToFractionConverter : IValueConverter
{
    public static readonly PercentToFractionConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is double percent ? Math.Clamp(percent / 100.0, 0.0, 1.0) : 0.0;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
