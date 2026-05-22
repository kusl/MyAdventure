using System.Globalization;
using System.Text;
using MyAdventure.Core.Numerics;

namespace MyAdventure.Core.Services;

/// <summary>
/// Formats numbers — both <see cref="double"/> and <see cref="BigDouble"/> —
/// into readable abbreviated strings.
/// <para>
/// Below 1000: two decimal places (<c>999.99</c>).
/// 1000 to 1e36: metric suffix (K, M, B, T, Qa, Qi, Sx, Sp, O, N, D),
/// e.g. <c>1234</c> → <c>"1.23 K"</c>, <c>1.5e12</c> → <c>"1.50 T"</c>.
/// At or above 1e36 (and for any <c>BigDouble</c> whose exponent is past
/// the suffix table): scientific notation with Unicode superscript exponents,
/// e.g. <c>7.53 × 10⁴⁰</c>. Keeps very large values short and readable
/// without inventing exotic suffixes.
/// </para>
/// <para>
/// The <see cref="BigDouble"/> overload is what allows the UI to display
/// progression deep into the post-double range (10⁵⁰⁰, 10¹⁰⁰⁰, …) with
/// the same conventions, instead of saturating at the prior 10²⁰⁰ ceiling.
/// </para>
/// <para>
/// Non-finite values are handled defensively so they can never propagate
/// crashes or gibberish through the UI:
/// </para>
/// <list type="bullet">
///   <item><c>+∞</c> → <c>"∞"</c></item>
///   <item><c>-∞</c> → <c>"-∞"</c></item>
///   <item><c>NaN</c> → <c>"?"</c></item>
/// </list>
/// </summary>
public static class NumberFormatter
{
    /// <summary>
    /// Suffix table, ordered highest-to-lowest so the first matching
    /// threshold wins. Caps at 1e33 (Decillion); values past
    /// <see cref="ScientificThreshold"/> fall through to scientific notation.
    /// </summary>
    private static readonly (long exponent, string suffix)[] Suffixes =
    [
        (33, "D"), (30, "N"), (27, "O"), (24, "Sp"),
        (21, "Sx"), (18, "Qi"), (15, "Qa"), (12, "T"),
        (9, "B"), (6, "M"), (3, "K")
    ];

    /// <summary>
    /// Threshold at which we switch from suffix notation to scientific notation.
    /// Picked as 10³⁶ so the last suffix bucket "D" tops out cleanly at
    /// "999.99 D" instead of overflowing into "1000.00 D".
    /// </summary>
    private const long ScientificThresholdExponent = 36;

    // Unicode superscript digits 0-9.
    private static readonly char[] SuperscriptDigits =
    [
        '\u2070', // ⁰
        '\u00B9', // ¹
        '\u00B2', // ²
        '\u00B3', // ³
        '\u2074', // ⁴
        '\u2075', // ⁵
        '\u2076', // ⁶
        '\u2077', // ⁷
        '\u2078', // ⁸
        '\u2079', // ⁹
    ];

    private const char SuperscriptMinus = '\u207B'; // ⁻

    /// <summary>
    /// Render a double as a human-friendly string. See class summary for
    /// the precise rules — including how Infinity and NaN are handled.
    /// </summary>
    public static string Format(double value)
    {
        if (double.IsNaN(value)) return "?";
        if (double.IsPositiveInfinity(value)) return "\u221E";
        if (double.IsNegativeInfinity(value)) return "-\u221E";

        if (value < 0) return $"-{Format(-value)}";
        if (value < 1000) return value.ToString("F2", CultureInfo.InvariantCulture);

        // Defer to the BigDouble path so suffix selection and scientific-
        // notation rendering are implemented once.
        return Format(new BigDouble(value));
    }

    /// <summary>
    /// Render a <see cref="BigDouble"/> using the same rules as the
    /// <see cref="Format(double)"/> overload. Values past the suffix table
    /// fall through to scientific notation regardless of how large.
    /// </summary>
    public static string Format(BigDouble value)
    {
        if (value.IsNaN) return "?";
        if (value.IsInfinity) return value.Sign < 0 ? "-\u221E" : "\u221E";
        if (value.IsZero) return "0.00";
        if (value.Sign < 0) return $"-{Format(value.Abs())}";

        // Small values: defer to the double formatter for the "999.99" rendering.
        if (value < new BigDouble(1000))
        {
            var asDouble = value.ToDouble();
            return asDouble.ToString("F2", CultureInfo.InvariantCulture);
        }

        // Past the suffix table → scientific notation.
        if (value.Exponent >= ScientificThresholdExponent)
        {
            return FormatScientific(value);
        }

        // Find the largest suffix whose exponent is at or below the value's.
        foreach (var (exponent, suffix) in Suffixes)
        {
            if (value.Exponent >= exponent)
            {
                // Shift the mantissa by (value.Exponent - exponent) so the
                // displayed number is in [1, 1000). E.g. 12.3 K means
                // mantissa was 1.23 with value.Exponent = 4 and suffix.exponent = 3.
                var shift = value.Exponent - exponent;
                var scaled = value.Mantissa * Math.Pow(10, shift);
                return $"{scaled.ToString("F2", CultureInfo.InvariantCulture)} {suffix}";
            }
        }

        // Defensive fallback — unreachable because the smallest suffix is
        // at exponent 3 and we already short-circuited values < 1000.
        return value.ToDouble().ToString("F2", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Format a double using scientific notation with Unicode superscript exponents.
    /// </summary>
    public static string FormatScientific(double value)
    {
        if (double.IsNaN(value)) return "?";
        if (double.IsPositiveInfinity(value)) return "\u221E";
        if (double.IsNegativeInfinity(value)) return "-\u221E";
        if (value == 0) return "0";
        if (value < 0) return $"-{FormatScientific(-value)}";

        var exponent = (int)Math.Floor(Math.Log10(value));
        var mantissa = value / Math.Pow(10, exponent);

        if (mantissa >= 10.0)
        {
            mantissa /= 10.0;
            exponent += 1;
        }

        var mantissaText = mantissa.ToString("F2", CultureInfo.InvariantCulture);
        return $"{mantissaText} \u00D7 10{ToSuperscript(exponent)}";
    }

    /// <summary>
    /// Format a <see cref="BigDouble"/> using scientific notation with
    /// Unicode superscript exponents.
    /// </summary>
    public static string FormatScientific(BigDouble value)
    {
        if (value.IsNaN) return "?";
        if (value.IsInfinity) return value.Sign < 0 ? "-\u221E" : "\u221E";
        if (value.IsZero) return "0";
        if (value.Sign < 0) return $"-{FormatScientific(value.Abs())}";

        var mantissaText = value.Mantissa.ToString("F2", CultureInfo.InvariantCulture);
        return $"{mantissaText} \u00D7 10{ToSuperscript(value.Exponent)}";
    }

    /// <summary>
    /// Convert an int to its Unicode superscript representation.
    /// </summary>
    public static string ToSuperscript(int n) => ToSuperscript((long)n);

    /// <summary>
    /// Convert a long to its Unicode superscript representation.
    /// Used by the BigDouble formatter where the exponent is a long.
    /// </summary>
    public static string ToSuperscript(long n)
    {
        if (n == 0) return SuperscriptDigits[0].ToString();

        var negative = n < 0;
        // Use unsigned negation to safely handle long.MinValue.
        var abs = negative ? (ulong)(-(n + 1)) + 1UL : (ulong)n;
        var digits = new Stack<char>();
        while (abs > 0)
        {
            digits.Push(SuperscriptDigits[abs % 10]);
            abs /= 10;
        }

        var sb = new StringBuilder(digits.Count + (negative ? 1 : 0));
        if (negative) sb.Append(SuperscriptMinus);
        while (digits.Count > 0) sb.Append(digits.Pop());
        return sb.ToString();
    }
}
