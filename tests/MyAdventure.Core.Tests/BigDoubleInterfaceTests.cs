using System.Globalization;
using System.Numerics;
using MyAdventure.Core.Numerics;
using Shouldly;

namespace MyAdventure.Core.Tests;

public class BigDoubleInterfaceTests
{
    private static T Sum<T>(IEnumerable<T> values) where T : IAdditionOperators<T, T, T>, IAdditiveIdentity<T, T>
    {
        var total = T.AdditiveIdentity;
        foreach (var value in values) total += value;
        return total;
    }

    private static T Max<T>(T a, T b) where T : IComparisonOperators<T, T, bool> => a > b ? a : b;

    [Fact]
    public void GenericMath_SumsThroughStaticAbstractMembers() =>
        Sum(new[] { new BigDouble(1), new BigDouble(2.5), new BigDouble(1.0, 3) }).ToDouble().ShouldBe(1003.5, tolerance: 1e-9);

    [Fact]
    public void GenericMath_ComparesThroughStaticAbstractMembers() =>
        Max(new BigDouble(1.0, 400), new BigDouble(9.0, 399)).ShouldBe(new BigDouble(1.0, 400));

    [Fact]
    public void Identities_AreZeroAndOne()
    {
        BigDouble.AdditiveIdentity.ShouldBe(BigDouble.Zero);
        BigDouble.MultiplicativeIdentity.ShouldBe(BigDouble.One);
    }

    [Fact]
    public void SpanParsable_ParsesCanonicalAndPlainForms()
    {
        BigDouble.Parse("2.5e700".AsSpan(), CultureInfo.InvariantCulture).ShouldBe(new BigDouble(2.5, 700));
        BigDouble.TryParse("1234.5".AsSpan(), CultureInfo.InvariantCulture, out var plain).ShouldBeTrue();
        plain.ShouldBe(new BigDouble(1234.5));
        BigDouble.TryParse("nope".AsSpan(), CultureInfo.InvariantCulture, out _).ShouldBeFalse();
    }

    [Fact]
    public void SpanFormattable_InterpolatesCanonicalForm()
    {
        var value = new BigDouble(6.25, 1234);
        $"{value}".ShouldBe("6.25e1234");

        Span<char> tooSmall = stackalloc char[3];
        value.TryFormat(tooSmall, out var written, default, CultureInfo.InvariantCulture).ShouldBeFalse();
        written.ShouldBe(0);
    }

    [Fact]
    public void Comparable_SortsMixedMagnitudes()
    {
        BigDouble[] values = [new(1.0, 300), BigDouble.Zero, new(-5), BigDouble.PositiveInfinity, new(3)];

        Array.Sort(values);

        values.ShouldBe(new[] { new BigDouble(-5), BigDouble.Zero, new BigDouble(3), new BigDouble(1.0, 300), BigDouble.PositiveInfinity });
    }
}
