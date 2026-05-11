using System.Globalization;
using System.Text;

namespace MyAdventure.Core.Services;

/// <summary>
/// Formats large numbers into readable abbreviated strings.
/// <para>
/// Below 1000: two decimal places, e.g. <c>999.99</c>.
/// </para>
/// <para>
/// Between 1000 and 1e36: metric-style suffix (K, M, B, T, Qa, Qi, Sx, Sp, O, N, D),
/// e.g. <c>1234</c> → <c>"1.23 K"</c>, <c>1.5e12</c> → <c>"1.50 T"</c>.
/// </para>
/// <para>
/// At or above 1e36 (where the suffix table runs out): scientific notation
/// with Unicode superscript exponents, e.g. <c>7.53e40</c> → <c>"7.53 × 10⁴⁰"</c>.
/// This keeps very large values readable without inventing new suffixes
/// that nobody recognizes.
/// </para>
/// <para>
/// Non-finite values are handled defensively so they can never propagate
/// crashes or gibberish through the UI:
/// <list type="bullet">
///   <item><c>double.PositiveInfinity</c> → <c>"∞"</c></item>
///   <item><c>double.NegativeInfinity</c> → <c>"-∞"</c></item>
///   <item><c>double.NaN</c> → <c>"?"</c></item>
/// </list>
/// The point of the NaN/Infinity branches is not that we expect to see
/// them — the game engine clamps its own state to keep values finite —
/// but that if a corrupted save or an arithmetic edge case ever produces
/// one, we render a glyph instead of dumping the literal string "Infinity"
/// into the UI or crashing a downstream JSON serializer that doesn't accept
/// non-finite doubles.
/// </para>
/// </summary>
public static class NumberFormatter
{
    /// <summary>
    /// Suffix table, ordered highest-to-lowest so the first matching
    /// threshold wins. Caps at 1e33 (Decillion); values at or above
    /// <see cref="ScientificThreshold"/> are formatted with
    /// <see cref="FormatScientific"/> instead.
    /// </summary>
    private static readonly (double threshold, string suffix)[] Suffixes =
    [
        (1e33, "D"), (1e30, "N"), (1e27, "O"), (1e24, "Sp"),
        (1e21, "Sx"), (1e18, "Qi"), (1e15, "Qa"), (1e12, "T"),
        (1e9, "B"), (1e6, "M"), (1e3, "K")
    ];

    /// <summary>
    /// Threshold at which we switch from suffix notation to scientific
    /// notation. Picked as 1000 × 1e33 = 1e36 so the last suffix bucket
    /// "D" tops out cleanly at "999.99 D" instead of overflowing into
    /// "1000.00 D" or worse.
    /// </summary>
    private const double ScientificThreshold = 1e36;

    // Unicode superscript digits 0-9 (U+2070, U+00B9, U+00B2, U+00B3, U+2074..U+2079).
    // Rendering with these means callers don't need a rich-text layout
    // to display "10⁴⁰".
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
        // Defensive: non-finite values must never leak through the UI or
        // into JSON. Glyph stand-ins are returned instead.
        if (double.IsNaN(value)) return "?";
        if (double.IsPositiveInfinity(value)) return "\u221E";  // ∞
        if (double.IsNegativeInfinity(value)) return "-\u221E"; // -∞

        if (value < 0) return $"-{Format(-value)}";
        if (value < 1000) return value.ToString("F2", CultureInfo.InvariantCulture);

        // Past the highest named suffix, switch to scientific notation
        // rather than emitting "1000000.00 D" or inventing new suffixes
        // that aren't widely recognized.
        if (value >= ScientificThreshold) return FormatScientific(value);

        foreach (var (threshold, suffix) in Suffixes)
        {
            if (value >= threshold)
            {
                var scaled = (value / threshold).ToString("F2", CultureInfo.InvariantCulture);
                return $"{scaled} {suffix}";
            }
        }

        // Defensive fallback — unreachable in practice because the suffix
        // table covers all of [1000, ScientificThreshold). Kept so the
        // method has a guaranteed return on every code path.
        return value.ToString("F2", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Format a value using scientific notation with Unicode superscript
    /// exponents. The mantissa is rendered with two decimals; the exponent
    /// becomes superscript digits so the result reads naturally
    /// (e.g. <c>"7.53 × 10⁴⁰"</c>) without needing rich-text layout.
    /// <para>
    /// Public so it can be unit-tested in isolation, and so callers that
    /// want scientific notation regardless of magnitude can opt in.
    /// </para>
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

        // Floating-point rounding can push the mantissa to exactly 10.0
        // (e.g. for 9.9999999...e40). Bump the exponent in that case so
        // the displayed mantissa stays in [1, 10).
        if (mantissa >= 10.0)
        {
            mantissa /= 10.0;
            exponent += 1;
        }

        var mantissaText = mantissa.ToString("F2", CultureInfo.InvariantCulture);
        return $"{mantissaText} \u00D7 10{ToSuperscript(exponent)}";
    }

    /// <summary>
    /// Convert an integer to its Unicode superscript representation.
    /// e.g. <c>40</c> → <c>"⁴⁰"</c>, <c>-3</c> → <c>"⁻³"</c>.
    /// </summary>
    public static string ToSuperscript(int n)
    {
        if (n == 0) return SuperscriptDigits[0].ToString();

        var negative = n < 0;
        // Use long to safely negate int.MinValue without overflow.
        var abs = negative ? -(long)n : n;
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
