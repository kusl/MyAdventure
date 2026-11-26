using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Numerics;
using System.Text.Json.Serialization;

namespace MyAdventure.Core.Numerics;

[JsonConverter(typeof(BigDoubleJsonConverter))]
public readonly struct BigDouble :
    IEquatable<BigDouble>,
    IComparable<BigDouble>,
    IComparable,
    ISpanFormattable,
    ISpanParsable<BigDouble>,
    IComparisonOperators<BigDouble, BigDouble, bool>,
    IAdditionOperators<BigDouble, BigDouble, BigDouble>,
    ISubtractionOperators<BigDouble, BigDouble, BigDouble>,
    IMultiplyOperators<BigDouble, BigDouble, BigDouble>,
    IDivisionOperators<BigDouble, BigDouble, BigDouble>,
    IUnaryNegationOperators<BigDouble, BigDouble>,
    IAdditiveIdentity<BigDouble, BigDouble>,
    IMultiplicativeIdentity<BigDouble, BigDouble>
{
    private const long AdditionPrecisionGap = 17;

    public double Mantissa { get; }

    public long Exponent { get; }

    public static BigDouble Zero { get; } = new(0.0, 0, normalize: false);

    public static BigDouble One { get; } = new(1.0, 0, normalize: false);

    public static BigDouble PositiveInfinity { get; } = new(double.PositiveInfinity, 0, normalize: false);

    public static BigDouble NegativeInfinity { get; } = new(double.NegativeInfinity, 0, normalize: false);

    public static BigDouble NaN { get; } = new(double.NaN, 0, normalize: false);

    public static BigDouble AdditiveIdentity => Zero;

    public static BigDouble MultiplicativeIdentity => One;

    public BigDouble(double value)
    {
        if (!double.IsFinite(value) || value == 0.0)
        {
            Mantissa = value == 0.0 ? 0.0 : value;
            Exponent = 0;
            return;
        }

        var exp = (long)Math.Floor(Math.Log10(Math.Abs(value)));
        var mant = value / Math.Pow(10, exp);

        if (Math.Abs(mant) >= 10.0) { mant /= 10.0; exp += 1; }
        else if (Math.Abs(mant) < 1.0) { mant *= 10.0; exp -= 1; }

        Mantissa = mant;
        Exponent = exp;
    }

    public BigDouble(double mantissa, long exponent, bool normalize = true)
    {
        if (!normalize)
        {
            Mantissa = mantissa;
            Exponent = exponent;
            return;
        }

        (Mantissa, Exponent) = NormalizePair(mantissa, exponent);
    }

    public bool IsZero => Mantissa == 0.0;

    public bool IsNaN => double.IsNaN(Mantissa);

    public bool IsInfinity => double.IsInfinity(Mantissa);

    public bool IsFinite => double.IsFinite(Mantissa);

    public int Sign => IsNaN ? 0 : Math.Sign(Mantissa);

    public BigDouble Abs() => new(Math.Abs(Mantissa), Exponent, normalize: false);

    public static BigDouble operator -(BigDouble value) => new(-value.Mantissa, value.Exponent, normalize: false);

    public static BigDouble operator +(BigDouble a, BigDouble b)
    {
        if (a.IsNaN || b.IsNaN) return NaN;
        if (a.IsZero) return b;
        if (b.IsZero) return a;
        if (a.IsInfinity || b.IsInfinity)
        {
            if (a.IsInfinity && b.IsInfinity && a.Sign != b.Sign) return NaN;
            return a.IsInfinity ? a : b;
        }

        var (large, small) = a.Exponent >= b.Exponent ? (a, b) : (b, a);
        var gap = large.Exponent - small.Exponent;
        if (gap > AdditionPrecisionGap) return large;

        return new BigDouble(large.Mantissa + small.Mantissa / Math.Pow(10, gap), large.Exponent);
    }

    public static BigDouble operator -(BigDouble a, BigDouble b) => a + -b;

    public static BigDouble operator *(BigDouble a, BigDouble b)
    {
        if (a.IsNaN || b.IsNaN) return NaN;
        if (a.IsZero || b.IsZero) return a.IsInfinity || b.IsInfinity ? NaN : Zero;
        if (a.IsInfinity || b.IsInfinity || WouldOverflow(a.Exponent, b.Exponent)) return SignedInfinity(a.Sign * b.Sign);

        return new BigDouble(a.Mantissa * b.Mantissa, a.Exponent + b.Exponent);
    }

    public static BigDouble operator /(BigDouble a, BigDouble b)
    {
        if (a.IsNaN || b.IsNaN) return NaN;
        if (b.IsZero) return a.IsZero ? NaN : SignedInfinity(a.Sign);
        if (a.IsZero) return Zero;
        if (a.IsInfinity && b.IsInfinity) return NaN;
        if (a.IsInfinity) return SignedInfinity(a.Sign * b.Sign);
        if (b.IsInfinity) return Zero;

        if (b.Exponent > 0 && a.Exponent < long.MinValue + b.Exponent) return Zero;
        if (b.Exponent < 0 && a.Exponent > long.MaxValue + b.Exponent) return SignedInfinity(a.Sign * b.Sign);

        return new BigDouble(a.Mantissa / b.Mantissa, a.Exponent - b.Exponent);
    }

    public BigDouble Pow(double power)
    {
        if (IsNaN || double.IsNaN(power)) return NaN;
        if (power == 0.0) return One;
        if (power == 1.0) return this;
        if (IsZero) return power > 0 ? Zero : PositiveInfinity;
        if (IsInfinity)
        {
            if (power <= 0) return Zero;
            return Sign > 0 || power % 2 == 0 ? PositiveInfinity : NegativeInfinity;
        }

        if (Sign < 0 && power != Math.Floor(power)) return NaN;

        var negativeResult = Sign < 0 && power % 2 != 0;
        var resultLog = (Math.Log10(Math.Abs(Mantissa)) + Exponent) * power;

        if (resultLog > long.MaxValue) return negativeResult ? NegativeInfinity : PositiveInfinity;
        if (resultLog < long.MinValue) return Zero;

        var newExp = (long)Math.Floor(resultLog);
        var newMantissa = Math.Pow(10, resultLog - newExp);

        return new BigDouble(negativeResult ? -newMantissa : newMantissa, newExp);
    }

    public BigDouble Pow(int power) => Pow((double)power);

    public BigDouble Sqrt()
    {
        if (IsNaN || Sign < 0) return NaN;
        if (IsZero) return Zero;
        if (IsInfinity) return PositiveInfinity;
        return Pow(0.5);
    }

    public double Log10()
    {
        if (IsNaN || Sign <= 0) return double.NaN;
        if (IsInfinity) return double.PositiveInfinity;
        return Math.Log10(Mantissa) + Exponent;
    }

    public BigDouble Floor()
    {
        if (!IsFinite || IsZero || Exponent >= 16) return this;
        return new BigDouble(Math.Floor(ToDouble()));
    }

    public double ToDouble()
    {
        if (!IsFinite) return Mantissa;
        if (IsZero) return 0.0;
        if (Exponent > 308) return Sign > 0 ? double.MaxValue : double.MinValue;
        if (Exponent < -323) return 0.0;
        return Mantissa * Math.Pow(10, Exponent);
    }

    public int CompareTo(BigDouble other)
    {
        if (IsNaN || other.IsNaN) return 0;

        if (IsInfinity || other.IsInfinity)
        {
            if (IsInfinity && other.IsInfinity) return Sign.CompareTo(other.Sign);
            return IsInfinity ? Sign : -other.Sign;
        }

        if (IsZero && other.IsZero) return 0;
        if (IsZero) return -Math.Sign(other.Mantissa);
        if (other.IsZero) return Math.Sign(Mantissa);

        var sign = Sign;
        if (sign != other.Sign) return sign.CompareTo(other.Sign);

        var expCompare = Exponent.CompareTo(other.Exponent);
        if (expCompare != 0) return sign > 0 ? expCompare : -expCompare;

        return Mantissa.CompareTo(other.Mantissa);
    }

    public int CompareTo(object? obj) => obj switch
    {
        null => 1,
        BigDouble other => CompareTo(other),
        _ => throw new ArgumentException($"Object must be of type {nameof(BigDouble)}.", nameof(obj)),
    };

    public bool Equals(BigDouble other)
    {
        if (IsNaN || other.IsNaN) return false;
        if (IsZero && other.IsZero) return true;
        return Mantissa == other.Mantissa && Exponent == other.Exponent;
    }

    public override bool Equals([NotNullWhen(true)] object? obj) => obj is BigDouble other && Equals(other);

    public override int GetHashCode() => IsZero ? 0 : HashCode.Combine(Mantissa, Exponent);

    public static bool operator ==(BigDouble a, BigDouble b) => a.Equals(b);
    public static bool operator !=(BigDouble a, BigDouble b) => !a.Equals(b);
    public static bool operator <(BigDouble a, BigDouble b) => a.CompareTo(b) < 0;
    public static bool operator >(BigDouble a, BigDouble b) => a.CompareTo(b) > 0;
    public static bool operator <=(BigDouble a, BigDouble b) => a.CompareTo(b) <= 0;
    public static bool operator >=(BigDouble a, BigDouble b) => a.CompareTo(b) >= 0;

    public static implicit operator BigDouble(double value) => new(value);
    public static implicit operator BigDouble(int value) => new(value);
    public static implicit operator BigDouble(long value) => new(value);

    public string ToCanonicalString()
    {
        if (IsNaN) return "NaN";
        if (double.IsPositiveInfinity(Mantissa)) return "Infinity";
        if (double.IsNegativeInfinity(Mantissa)) return "-Infinity";
        if (IsZero) return "0";
        return string.Create(CultureInfo.InvariantCulture, $"{Mantissa:R}e{Exponent}");
    }

    public override string ToString() => ToCanonicalString();

    public string ToString(string? format, IFormatProvider? formatProvider) => ToCanonicalString();

    public bool TryFormat(Span<char> destination, out int charsWritten, ReadOnlySpan<char> format, IFormatProvider? provider)
    {
        var text = ToCanonicalString();
        if (text.TryCopyTo(destination))
        {
            charsWritten = text.Length;
            return true;
        }

        charsWritten = 0;
        return false;
    }

    public static BigDouble Parse(string? value) =>
        TryParse(value.AsSpan(), CultureInfo.InvariantCulture, out var result)
            ? result
            : throw new FormatException($"Could not parse '{value}' as {nameof(BigDouble)}");

    public static BigDouble Parse(string s, IFormatProvider? provider) => Parse(s);

    public static BigDouble Parse(ReadOnlySpan<char> s, IFormatProvider? provider) =>
        TryParse(s, provider, out var result)
            ? result
            : throw new FormatException($"Could not parse '{s}' as {nameof(BigDouble)}");

    public static bool TryParse(string? value, out BigDouble result) =>
        TryParse(value.AsSpan(), CultureInfo.InvariantCulture, out result);

    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out BigDouble result) =>
        TryParse(s.AsSpan(), provider, out result);

    public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out BigDouble result)
    {
        var trimmed = s.Trim();
        result = Zero;

        if (trimmed.IsEmpty) return true;
        if (trimmed.Equals("NaN", StringComparison.OrdinalIgnoreCase)) { result = NaN; return true; }
        if (trimmed.Equals("Infinity", StringComparison.OrdinalIgnoreCase)) { result = PositiveInfinity; return true; }
        if (trimmed.Equals("-Infinity", StringComparison.OrdinalIgnoreCase)) { result = NegativeInfinity; return true; }

        var eIdx = trimmed.IndexOf('e');
        if (eIdx > 0 && eIdx < trimmed.Length - 1
            && long.TryParse(trimmed[(eIdx + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var exp)
            && double.TryParse(trimmed[..eIdx], NumberStyles.Float, CultureInfo.InvariantCulture, out var mant))
        {
            result = new BigDouble(mant, exp);
            return true;
        }

        if (double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
        {
            result = new BigDouble(d);
            return true;
        }

        return false;
    }

    private static (double Mantissa, long Exponent) NormalizePair(double mantissa, long exponent)
    {
        if (!double.IsFinite(mantissa)) return (mantissa, 0);
        if (mantissa == 0.0) return (0.0, 0);

        var abs = Math.Abs(mantissa);
        if (abs is >= 1.0 and < 10.0) return (mantissa, exponent);

        var delta = (long)Math.Floor(Math.Log10(abs));
        var newExp = exponent + delta;
        var newMantissa = mantissa / Math.Pow(10, delta);

        if (Math.Abs(newMantissa) >= 10.0) { newMantissa /= 10.0; newExp += 1; }
        else if (Math.Abs(newMantissa) < 1.0 && newMantissa != 0.0) { newMantissa *= 10.0; newExp -= 1; }

        return (newMantissa, newExp);
    }

    private static bool WouldOverflow(long a, long b) =>
        (b > 0 && a > long.MaxValue - b) || (b < 0 && a < long.MinValue - b);

    private static BigDouble SignedInfinity(int sign) => sign > 0 ? PositiveInfinity : NegativeInfinity;
}
