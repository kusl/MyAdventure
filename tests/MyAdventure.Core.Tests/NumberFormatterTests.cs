using MyAdventure.Core.Services;
using Shouldly;

namespace MyAdventure.Core.Tests;

public class NumberFormatterTests
{
    [Theory]
    [InlineData(0, "0.00")]
    [InlineData(999.99, "999.99")]
    [InlineData(1000, "1.00 K")]
    [InlineData(1500, "1.50 K")]
    [InlineData(1_000_000, "1.00 M")]
    [InlineData(1_500_000_000, "1.50 B")]
    [InlineData(1e12, "1.00 T")]
    [InlineData(2.5e15, "2.50 Qa")]
    public void Format_ShouldReturnExpectedSuffix(double input, string expected) =>
        NumberFormatter.Format(input).ShouldBe(expected);

    [Fact]
    public void Format_NegativeNumbers_ShouldIncludeMinus() =>
        NumberFormatter.Format(-5000).ShouldStartWith("-");
}
