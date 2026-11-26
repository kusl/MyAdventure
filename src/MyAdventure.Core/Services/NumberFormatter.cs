using System.Globalization;
using MyAdventure.Core.Numerics;

namespace MyAdventure.Core.Services;

public static class NumberFormatter
{
    private const long ScientificThresholdExponent = 36;
    private const string Infinity = "∞";
    private const char SuperscriptMinus = '⁻';

    private static readonly (long Exponent, string Suffix)[] Suffixes =
    [
        (33, "D"), (30, "N"), (27, "O"), (24, "Sp"),
        (21, "Sx"), (18, "Qi"), (15, "Qa"), (12, "T"),
        (9, "B"), (6, "M"), (3, "K"),
    ];

    private static ReadOnlySpan<char> SuperscriptDigits => "⁰¹²³⁴⁵⁶⁷⁸⁹";

    public static string Format(double value) => value switch
    {
        double.NaN => "?",
        double.PositiveInfinity => Infinity,
        double.NegativeInfinity => $"-{Infinity}",
        < 0 => $"-{Format(-value)}",
        < 1000 => Fixed(value),
        _ => Format(new BigDouble(value)),
    };

    public static string Format(BigDouble value)
    {
        if (value.IsNaN) return "?";
        if (value.IsInfinity) return value.Sign < 0 ? $"-{Infinity}" : Infinity;
        if (value.IsZero) return "0.00";
        if (value.Sign < 0) return $"-{Format(value.Abs())}";
        if (value < new BigDouble(1000)) return Fixed(value.ToDouble());
        if (value.Exponent >= ScientificThresholdExponent) return FormatScientific(value);

        foreach (var (exponent, suffix) in Suffixes)
        {
            if (value.Exponent >= exponent)
            {
                return $"{Fixed(value.Mantissa * Math.Pow(10, value.Exponent - exponent))} {suffix}";
            }
        }

        return Fixed(value.ToDouble());
    }

    public static string FormatScientific(double value)
    {
        switch (value)
        {
            case double.NaN: return "?";
            case double.PositiveInfinity: return Infinity;
            case double.NegativeInfinity: return $"-{Infinity}";
            case 0: return "0";
            case < 0: return $"-{FormatScientific(-value)}";
        }

        var exponent = (int)Math.Floor(Math.Log10(value));
        var mantissa = value / Math.Pow(10, exponent);
        if (mantissa >= 10.0)
        {
            mantissa /= 10.0;
            exponent += 1;
        }

        return $"{Fixed(mantissa)} × 10{ToSuperscript(exponent)}";
    }

    public static string FormatScientific(BigDouble value)
    {
        if (value.IsNaN) return "?";
        if (value.IsInfinity) return value.Sign < 0 ? $"-{Infinity}" : Infinity;
        if (value.IsZero) return "0";
        if (value.Sign < 0) return $"-{FormatScientific(value.Abs())}";

        return $"{Fixed(value.Mantissa)} × 10{ToSuperscript(value.Exponent)}";
    }

    public static string ToSuperscript(int n) => ToSuperscript((long)n);

    public static string ToSuperscript(long n)
    {
        var negative = n < 0;
        var digits = n == 0 ? "0" : n.ToString(CultureInfo.InvariantCulture).TrimStart('-');
        return string.Create(digits.Length + (negative ? 1 : 0), (digits, negative), static (span, state) =>
        {
            var offset = 0;
            if (state.negative) span[offset++] = SuperscriptMinus;
            foreach (var digit in state.digits) span[offset++] = SuperscriptDigits[digit - '0'];
        });
    }

    private static string Fixed(double value) => value.ToString("F2", CultureInfo.InvariantCulture);
}
