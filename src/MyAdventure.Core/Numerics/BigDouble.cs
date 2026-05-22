using System.Globalization;

namespace MyAdventure.Core.Numerics;

/// <summary>
/// A floating-point number with a (signed) double mantissa and a long
/// exponent in base 10, designed for idle-game progression where values
/// routinely exceed <see cref="double.MaxValue"/> (~1.8 × 10³⁰⁸) and where
/// 15-16 significant decimal digits of precision is more than enough.
///
/// <para>
/// Internal invariants (maintained by every operation through
/// <see cref="Normalize"/>):
/// <list type="bullet">
///   <item>For non-zero values, <c>|Mantissa|</c> is in the half-open
///         interval <c>[1.0, 10.0)</c>.</item>
///   <item>The value zero is represented exactly by <c>Mantissa == 0.0</c>
///         and <c>Exponent == 0</c> — never any other combination.</item>
///   <item>Sign lives on the mantissa.</item>
/// </list>
/// </para>
///
/// <para>
/// Range: <c>±9.9999... × 10^(long.MaxValue)</c>. Effectively unbounded
/// for any conceivable idle-game progression — at one purchase per
/// microsecond starting at the Big Bang you would not reach this cap.
/// Operations that would push the exponent past <see cref="long.MaxValue"/>
/// (e.g. exponentiating a huge value to a huge power) saturate at
/// <see cref="PositiveInfinity"/>/<see cref="NegativeInfinity"/>; NaN
/// inputs propagate to <see cref="NaN"/>.
/// </para>
///
/// <para>
/// Persistence format: the canonical round-trippable string is
/// <c>"&lt;mantissa&gt;e&lt;exponent&gt;"</c> (e.g. <c>"1.5e200"</c>), produced by
/// <see cref="ToCanonicalString"/> and parsed by <see cref="Parse"/>. This
/// is what SQLite stores and what export/import use — never the
/// human-readable <see cref="ToString"/> rendering.
/// </para>
/// </summary>
public readonly struct BigDouble : IEquatable<BigDouble>, IComparable<BigDouble>
{
    /// <summary>Mantissa in <c>[1.0, 10.0)</c> for non-zero values; exactly 0 for zero.</summary>
    public double Mantissa { get; }

    /// <summary>Base-10 exponent. Always 0 when <see cref="Mantissa"/> is 0.</summary>
    public long Exponent { get; }

    /// <summary>Zero — additive identity.</summary>
    public static BigDouble Zero { get; } = new(0.0, 0, normalize: false);

    /// <summary>One — multiplicative identity.</summary>
    public static BigDouble One { get; } = new(1.0, 0, normalize: false);

    /// <summary>Positive infinity sentinel. Saturating; arithmetic with it returns infinity (except 0×∞ → NaN).</summary>
    public static BigDouble PositiveInfinity { get; } = new(double.PositiveInfinity, 0, normalize: false);

    /// <summary>Negative infinity sentinel.</summary>
    public static BigDouble NegativeInfinity { get; } = new(double.NegativeInfinity, 0, normalize: false);

    /// <summary>Not-a-number sentinel. Propagates through every operation.</summary>
    public static BigDouble NaN { get; } = new(double.NaN, 0, normalize: false);

    /// <summary>
    /// Construct from a normal double. NaN, Infinity, and zero are preserved exactly.
    /// </summary>
    public BigDouble(double value)
    {
        if (double.IsNaN(value)) { Mantissa = double.NaN; Exponent = 0; return; }
        if (double.IsPositiveInfinity(value)) { Mantissa = double.PositiveInfinity; Exponent = 0; return; }
        if (double.IsNegativeInfinity(value)) { Mantissa = double.NegativeInfinity; Exponent = 0; return; }
        if (value == 0.0) { Mantissa = 0.0; Exponent = 0; return; }

        var absValue = Math.Abs(value);
        var exp = (long)Math.Floor(Math.Log10(absValue));
        // Math.Pow(10, exp) is exact for exp in the practical double range,
        // so the divide normalizes the mantissa precisely.
        var mant = value / Math.Pow(10, exp);

        // Rounding can leave mantissa just slightly outside [1, 10), e.g. 9.999999...
        // rounding to 10.0 or 0.999999... rounding to ~1.0. Re-normalize.
        if (Math.Abs(mant) >= 10.0) { mant /= 10.0; exp += 1; }
        else if (Math.Abs(mant) < 1.0) { mant *= 10.0; exp -= 1; }

        Mantissa = mant;
        Exponent = exp;
    }

    /// <summary>
    /// Construct from a mantissa-exponent pair. When <paramref name="normalize"/>
    /// is true (the default for the public surface), the pair is normalized so
    /// the mantissa lands in <c>[1, 10)</c>; pass false only for known-normalized
    /// inputs (e.g. constructing sentinel values internally).
    /// </summary>
    public BigDouble(double mantissa, long exponent, bool normalize = true)
    {
        if (!normalize)
        {
            Mantissa = mantissa;
            Exponent = exponent;
            return;
        }

        var (m, e) = NormalizePair(mantissa, exponent);
        Mantissa = m;
        Exponent = e;
    }

    private static (double mantissa, long exponent) NormalizePair(double mantissa, long exponent)
    {
        if (double.IsNaN(mantissa)) return (double.NaN, 0);
        if (double.IsPositiveInfinity(mantissa)) return (double.PositiveInfinity, 0);
        if (double.IsNegativeInfinity(mantissa)) return (double.NegativeInfinity, 0);
        if (mantissa == 0.0) return (0.0, 0);

        var abs = Math.Abs(mantissa);

        // Fast path for already-normalized mantissas.
        if (abs >= 1.0 && abs < 10.0) return (mantissa, exponent);

        // Pull magnitude into the exponent. log10 is well-defined for any
        // positive double, including subnormals, so we lean on it rather
        // than looping by factors of 10.
        var log = Math.Log10(abs);
        var delta = (long)Math.Floor(log);
        var newExp = exponent + delta;

        // Math.Pow(10, delta) is exact across the practical range we use.
        var newMantissa = mantissa / Math.Pow(10, delta);

        // Defensive re-snap: rounding at the edges can still leave the
        // mantissa just outside the half-open interval (e.g. 10.0 - ε
        // representing as 10.0).
        if (Math.Abs(newMantissa) >= 10.0) { newMantissa /= 10.0; newExp += 1; }
        else if (Math.Abs(newMantissa) < 1.0 && newMantissa != 0.0)
        {
            newMantissa *= 10.0;
            newExp -= 1;
        }

        return (newMantissa, newExp);
    }

    /// <summary>True if this is exactly zero.</summary>
    public bool IsZero => Mantissa == 0.0;

    /// <summary>True if mantissa is NaN.</summary>
    public bool IsNaN => double.IsNaN(Mantissa);

    /// <summary>True if mantissa is positive or negative infinity.</summary>
    public bool IsInfinity => double.IsInfinity(Mantissa);

    /// <summary>True if neither NaN nor infinity.</summary>
    public bool IsFinite => !IsNaN && !IsInfinity;

    /// <summary>Sign: -1, 0, or +1. Returns 0 for zero and NaN-safe.</summary>
    public int Sign => IsNaN ? 0 : Math.Sign(Mantissa);

    /// <summary>Absolute value.</summary>
    public BigDouble Abs() => new(Math.Abs(Mantissa), Exponent, normalize: false);

    /// <summary>Unary negation.</summary>
    public static BigDouble operator -(BigDouble value) =>
        new(-value.Mantissa, value.Exponent, normalize: false);

    /// <summary>
    /// Addition. Values whose exponents differ by more than ~16 absorb the
    /// smaller (limited by double precision). NaN propagates.
    /// </summary>
    public static BigDouble operator +(BigDouble a, BigDouble b)
    {
        if (a.IsNaN || b.IsNaN) return NaN;
        if (a.IsZero) return b;
        if (b.IsZero) return a;
        if (a.IsInfinity || b.IsInfinity)
        {
            // +∞ + -∞ → NaN; same-sign infinities → same infinity.
            if (a.IsInfinity && b.IsInfinity && a.Sign != b.Sign) return NaN;
            return a.IsInfinity ? a : b;
        }

        // Align to the larger exponent. If the gap is bigger than double
        // precision, the smaller value contributes nothing — return the
        // larger directly to skip a costly Math.Pow that would compute
        // exactly 0 anyway.
        var (large, small) = a.Exponent >= b.Exponent ? (a, b) : (b, a);
        var gap = large.Exponent - small.Exponent;
        if (gap > 17) return large;

        // Shift the smaller mantissa down by the gap and combine. The
        // result needs renormalization because the sum can land anywhere
        // in [0, ~20).
        var shiftedSmall = small.Mantissa / Math.Pow(10, gap);
        var sumMantissa = large.Mantissa + shiftedSmall;
        return new BigDouble(sumMantissa, large.Exponent);
    }

    /// <summary>Subtraction: <c>a - b</c>.</summary>
    public static BigDouble operator -(BigDouble a, BigDouble b) => a + (-b);

    /// <summary>
    /// Multiplication. Mantissas multiply, exponents add. Long-overflow on
    /// the exponent saturates to ±Infinity rather than wrapping.
    /// </summary>
    public static BigDouble operator *(BigDouble a, BigDouble b)
    {
        if (a.IsNaN || b.IsNaN) return NaN;
        if (a.IsZero || b.IsZero)
        {
            // 0 × ∞ is undefined per IEEE 754.
            if (a.IsInfinity || b.IsInfinity) return NaN;
            return Zero;
        }
        if (a.IsInfinity || b.IsInfinity)
        {
            // Sign by multiplication of the two component signs.
            return a.Sign * b.Sign > 0 ? PositiveInfinity : NegativeInfinity;
        }

        // The multiplied mantissa is in [1, 100); one normalization step
        // will pull it back into [1, 10).
        var mantissa = a.Mantissa * b.Mantissa;

        // Detect long-overflow before performing the add.
        if (WouldOverflow(a.Exponent, b.Exponent))
        {
            return a.Sign * b.Sign > 0 ? PositiveInfinity : NegativeInfinity;
        }

        return new BigDouble(mantissa, a.Exponent + b.Exponent);
    }

    /// <summary>Division.</summary>
    public static BigDouble operator /(BigDouble a, BigDouble b)
    {
        if (a.IsNaN || b.IsNaN) return NaN;
        if (b.IsZero)
        {
            if (a.IsZero) return NaN;
            return a.Sign > 0 ? PositiveInfinity : NegativeInfinity;
        }
        if (a.IsZero) return Zero;
        if (a.IsInfinity && b.IsInfinity) return NaN;
        if (a.IsInfinity) return a.Sign * b.Sign > 0 ? PositiveInfinity : NegativeInfinity;
        if (b.IsInfinity) return Zero;

        var mantissa = a.Mantissa / b.Mantissa;

        // Detect overflow on `a.Exponent - b.Exponent`. Direct subtraction
        // overflows long when b.Exponent is long.MinValue (negation wraps).
        // Compare-with-bound is overflow-safe.
        if (b.Exponent > 0 && a.Exponent < long.MinValue + b.Exponent) return Zero;
        if (b.Exponent < 0 && a.Exponent > long.MaxValue + b.Exponent)
        {
            return a.Sign * b.Sign > 0 ? PositiveInfinity : NegativeInfinity;
        }

        return new BigDouble(mantissa, a.Exponent - b.Exponent);
    }

    /// <summary>
    /// Long-overflow detection for exponent addition. Returns true if
    /// <c>a + b</c> would overflow <see cref="long.MaxValue"/> or
    /// underflow <see cref="long.MinValue"/>.
    /// </summary>
    private static bool WouldOverflow(long a, long b)
    {
        if (b > 0 && a > long.MaxValue - b) return true;
        if (b < 0 && a < long.MinValue - b) return true;
        return false;
    }

    /// <summary>
    /// <c>this ^ power</c>. Uses <c>10^(power × log10(this))</c> internally so
    /// even astronomical bases or exponents stay representable; saturates to
    /// infinity on overflow. Power 0 returns 1; negative powers return the
    /// reciprocal.
    /// </summary>
    public BigDouble Pow(double power)
    {
        if (IsNaN || double.IsNaN(power)) return NaN;
        if (power == 0.0) return One;
        if (power == 1.0) return this;
        if (IsZero) return power > 0 ? Zero : PositiveInfinity;
        if (IsInfinity)
        {
            if (power > 0) return Sign > 0 ? PositiveInfinity : (power % 2 == 0 ? PositiveInfinity : NegativeInfinity);
            return Zero;
        }

        // Negative bases with non-integer powers are not defined in the
        // reals; we punt to NaN rather than producing a complex number.
        if (Sign < 0 && power != Math.Floor(power)) return NaN;

        var sign = Sign;
        var absLog = Math.Log10(Math.Abs(Mantissa)) + Exponent;
        var resultLog = absLog * power;

        // Saturate if the result is outside the representable exponent range.
        if (resultLog > long.MaxValue) return sign < 0 && power % 2 != 0 ? NegativeInfinity : PositiveInfinity;
        if (resultLog < long.MinValue) return Zero;

        var newExp = (long)Math.Floor(resultLog);
        var fractional = resultLog - newExp;
        var newMantissa = Math.Pow(10, fractional);

        if (sign < 0 && power % 2 != 0) newMantissa = -newMantissa;

        return new BigDouble(newMantissa, newExp);
    }

    /// <summary>Integer-power overload; uses repeated multiplication for very small powers.</summary>
    public BigDouble Pow(int power) => Pow((double)power);

    /// <summary>Square root: defined for non-negative values; NaN for negatives.</summary>
    public BigDouble Sqrt()
    {
        if (IsNaN || Sign < 0) return NaN;
        if (IsZero) return Zero;
        if (IsInfinity) return PositiveInfinity;
        return Pow(0.5);
    }

    /// <summary>Base-10 logarithm of the absolute value. NaN for zero or negative.</summary>
    public double Log10()
    {
        if (IsNaN || Sign <= 0) return double.NaN;
        if (IsInfinity) return double.PositiveInfinity;
        return Math.Log10(Mantissa) + Exponent;
    }

    /// <summary>Floor toward negative infinity. Mostly identity at large magnitudes (exponent ≥ 16).</summary>
    public BigDouble Floor()
    {
        if (IsNaN || IsInfinity || IsZero) return this;
        // At very large exponents the value is already an integer in
        // double precision — every representable double in that range
        // happens to be a whole number, so Floor is identity.
        if (Exponent >= 16) return this;
        // For small exponents, convert to double, floor, convert back.
        return new BigDouble(Math.Floor(ToDouble()));
    }

    /// <summary>
    /// Convert to a normal double. Saturates to ±<see cref="double.MaxValue"/>
    /// for values outside the double range rather than producing Infinity.
    /// Used only by callers that genuinely need a double (e.g. UI progress
    /// fractions, log axes); for arithmetic always stay in BigDouble.
    /// </summary>
    public double ToDouble()
    {
        if (IsNaN) return double.NaN;
        if (IsInfinity) return Mantissa;
        if (IsZero) return 0.0;

        // double can represent up to ~1.8e308 magnitude. Past that, we
        // saturate at MaxValue / -MaxValue with the original sign rather
        // than overflowing to ±Infinity.
        if (Exponent > 308) return Sign > 0 ? double.MaxValue : double.MinValue;
        if (Exponent < -323) return 0.0;

        return Mantissa * Math.Pow(10, Exponent);
    }

    // -----------------------------------------------------------------
    // Comparison
    // -----------------------------------------------------------------

    public int CompareTo(BigDouble other)
    {
        if (IsNaN || other.IsNaN) return 0; // NaN is unordered; sorted at the bottom by convention.

        // Infinity must be handled BEFORE the exponent comparison below.
        // Internally the infinity sentinels are stored as (±∞ mantissa, 0
        // exponent); without this branch, the exponent-comparison fallback
        // would mistakenly find PositiveInfinity "smaller than" a finite
        // value with a high exponent (e.g. 1e100), corrupting every "can
        // I afford this?" check that involves an infinite cash value.
        if (IsInfinity || other.IsInfinity)
        {
            if (IsInfinity && other.IsInfinity)
                return Sign.CompareTo(other.Sign); // +∞ > -∞; equal infinities → 0
            return IsInfinity ? Sign : -other.Sign; // any infinity vs any finite
        }

        if (IsZero && other.IsZero) return 0;
        if (IsZero) return -Math.Sign(other.Mantissa);
        if (other.IsZero) return Math.Sign(Mantissa);

        // Different signs: positive > negative.
        var s = Sign;
        var os = other.Sign;
        if (s != os) return s.CompareTo(os);

        // Same sign: compare exponents first, then mantissa. For negative
        // values the order flips on exponent (more-negative exponent → larger negative number).
        var expCompare = Exponent.CompareTo(other.Exponent);
        if (expCompare != 0) return s > 0 ? expCompare : -expCompare;

        return Mantissa.CompareTo(other.Mantissa);
    }

    public bool Equals(BigDouble other)
    {
        if (IsNaN || other.IsNaN) return false; // NaN ≠ NaN per IEEE 754.
        if (IsZero && other.IsZero) return true;
        return Mantissa == other.Mantissa && Exponent == other.Exponent;
    }

    public override bool Equals(object? obj) => obj is BigDouble bd && Equals(bd);

    public override int GetHashCode() => IsZero ? 0 : HashCode.Combine(Mantissa, Exponent);

    public static bool operator ==(BigDouble a, BigDouble b) => a.Equals(b);
    public static bool operator !=(BigDouble a, BigDouble b) => !a.Equals(b);
    public static bool operator <(BigDouble a, BigDouble b) => a.CompareTo(b) < 0;
    public static bool operator >(BigDouble a, BigDouble b) => a.CompareTo(b) > 0;
    public static bool operator <=(BigDouble a, BigDouble b) => a.CompareTo(b) <= 0;
    public static bool operator >=(BigDouble a, BigDouble b) => a.CompareTo(b) >= 0;

    // -----------------------------------------------------------------
    // Implicit conversions from primitive numeric types so business
    // code can write `cash + 5.0` or `1000 * baseRevenue` naturally.
    // Conversion FROM BigDouble back to double is explicit (via ToDouble)
    // because it can lose magnitude.
    // -----------------------------------------------------------------
    public static implicit operator BigDouble(double value) => new(value);
    public static implicit operator BigDouble(int value) => new(value);
    public static implicit operator BigDouble(long value) => new(value);

    // -----------------------------------------------------------------
    // Serialization
    // -----------------------------------------------------------------

    /// <summary>
    /// Canonical round-trippable form: <c>"&lt;mantissa&gt;e&lt;exponent&gt;"</c>
    /// with the mantissa formatted using <c>R</c> (round-trip) so that
    /// <see cref="Parse"/> of the output reconstructs the same value bit-for-bit.
    /// NaN → <c>"NaN"</c>; ±Infinity → <c>"Infinity"</c>/<c>"-Infinity"</c>;
    /// zero → <c>"0"</c>.
    /// <para>
    /// This is the format SQLite stores (via the EF Core value converter)
    /// and the format the import/export feature uses. Distinct from
    /// <see cref="ToString"/>, which is for display only.
    /// </para>
    /// </summary>
    public string ToCanonicalString()
    {
        if (IsNaN) return "NaN";
        if (double.IsPositiveInfinity(Mantissa)) return "Infinity";
        if (double.IsNegativeInfinity(Mantissa)) return "-Infinity";
        if (IsZero) return "0";

        var m = Mantissa.ToString("R", CultureInfo.InvariantCulture);
        return $"{m}e{Exponent.ToString(CultureInfo.InvariantCulture)}";
    }

    /// <summary>
    /// Parse a canonical string (<see cref="ToCanonicalString"/> output) or
    /// any plain-double string (e.g. "1234.5"). Returns
    /// <see cref="Zero"/> for null/empty input, and throws
    /// <see cref="FormatException"/> for unparseable strings.
    /// </summary>
    public static BigDouble Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return Zero;

        var trimmed = value.Trim();

        if (trimmed.Equals("NaN", StringComparison.OrdinalIgnoreCase)) return NaN;
        if (trimmed.Equals("Infinity", StringComparison.OrdinalIgnoreCase)) return PositiveInfinity;
        if (trimmed.Equals("-Infinity", StringComparison.OrdinalIgnoreCase)) return NegativeInfinity;

        // Look for the "e" separator that distinguishes our canonical form
        // from a plain double. Note: a "+1.5" or "-1.5" has no 'e', and
        // plain doubles like "1.5e10" use 'e' for their native exponent —
        // we let double.Parse handle that case uniformly below.
        var eIdx = trimmed.IndexOf('e');
        if (eIdx > 0 && eIdx < trimmed.Length - 1)
        {
            // Try parsing the exponent as a long. If it fits in long, treat
            // the string as our canonical form; otherwise fall through to
            // double.Parse (which handles small-exponent native doubles).
            var mantissaPart = trimmed[..eIdx];
            var exponentPart = trimmed[(eIdx + 1)..];
            if (long.TryParse(exponentPart, NumberStyles.Integer, CultureInfo.InvariantCulture, out var exp)
                && double.TryParse(mantissaPart, NumberStyles.Float, CultureInfo.InvariantCulture, out var mant))
            {
                return new BigDouble(mant, exp);
            }
        }

        // Fallback: parse as a normal double (handles things like "1234.5"
        // and short scientific notation like "1e10").
        if (double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
        {
            return new BigDouble(d);
        }

        throw new FormatException($"Could not parse '{value}' as BigDouble");
    }

    /// <summary>
    /// Same as <see cref="Parse"/> but returns false instead of throwing.
    /// </summary>
    public static bool TryParse(string? value, out BigDouble result)
    {
        try
        {
            result = Parse(value);
            return true;
        }
        catch
        {
            result = Zero;
            return false;
        }
    }

    /// <summary>
    /// Display rendering — same as <see cref="ToCanonicalString"/> for now,
    /// but kept as a separate method so future changes to canonical-form
    /// can't accidentally break <c>NumberFormatter.Format(BigDouble)</c>'s
    /// debugger-fallback path.
    /// </summary>
    public override string ToString() => ToCanonicalString();
}
