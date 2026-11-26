using System.Text;
using System.Text.Json;
using MyAdventure.Core.Numerics;
using MyAdventure.Core.Saves;
using Shouldly;

namespace MyAdventure.Core.Tests;

public class SaveCodecTests
{
    private static readonly DateTime ExportedAt = new(2026, 11, 26, 12, 0, 0, DateTimeKind.Utc);

    private static string Json(string encoded) => Encoding.UTF8.GetString(Convert.FromBase64String(encoded));

    private static string Encode(string json) => Convert.ToBase64String(Encoding.UTF8.GetBytes(json));

    [Fact]
    public void Encode_WritesVersionTwoShapeInStableOrder()
    {
        var snapshot = new GameSnapshot
        {
            Cash = new BigDouble(42.5),
            LifetimeEarnings = new BigDouble(1.0, 500),
            AngelInvestors = new BigDouble(7),
            PrestigeCount = 2,
            Owned = new Dictionary<string, long> { ["lemonade"] = 3 },
            Managers = new Dictionary<string, bool> { ["lemonade"] = true },
        };

        var json = Json(SaveCodec.Encode(snapshot, ExportedAt));

        json.ShouldBe("""{"v":2,"cash":"4.25e1","lifetime":"1e500","angels":"7e0","prestige":2,"businesses":{"lemonade":3},"managers":{"lemonade":true},"timestamp":"2026-11-26T12:00:00.0000000Z"}""");
    }

    [Fact]
    public void EncodeThenDecode_RoundTrips()
    {
        var snapshot = new GameSnapshot
        {
            Cash = new BigDouble(7.5, 900),
            LifetimeEarnings = new BigDouble(1.25, 1200),
            AngelInvestors = new BigDouble(3.0, 40),
            PrestigeCount = 11,
            Owned = new Dictionary<string, long> { ["shrimp"] = 2_270 },
            Managers = new Dictionary<string, bool> { ["shrimp"] = true },
        };

        SaveCodec.TryDecode(SaveCodec.Encode(snapshot, ExportedAt), out var decoded).ShouldBeTrue();

        decoded.Cash.ShouldBe(snapshot.Cash);
        decoded.LifetimeEarnings.ShouldBe(snapshot.LifetimeEarnings);
        decoded.AngelInvestors.ShouldBe(snapshot.AngelInvestors);
        decoded.PrestigeCount.ShouldBe(11);
        decoded.Owned["shrimp"].ShouldBe(2_270);
        decoded.Managers["shrimp"].ShouldBeTrue();
    }

    [Fact]
    public void Decode_LegacyNumbers_AreAccepted()
    {
        SaveCodec.TryDecode(Encode("""{"v":1,"cash":12345.67,"lifetime":1e6,"angels":50,"prestige":2}"""), out var decoded).ShouldBeTrue();

        decoded.Cash.ToDouble().ShouldBe(12345.67, tolerance: 1e-9);
        decoded.AngelInvestors.ToDouble().ShouldBe(50);
        decoded.Owned.ShouldBeEmpty();
    }

    [Fact]
    public void Decode_UnparseableAmount_FallsBackToZero()
    {
        SaveCodec.TryDecode(Encode("""{"v":2,"cash":"lots","lifetime":null,"angels":{"x":1}}"""), out var decoded).ShouldBeTrue();

        decoded.Cash.IsZero.ShouldBeTrue();
        decoded.LifetimeEarnings.IsZero.ShouldBeTrue();
        decoded.AngelInvestors.IsZero.ShouldBeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-valid-base64!!!")]
    [InlineData("bm90IGpzb24=")]
    [InlineData("bnVsbA==")]
    public void Decode_Garbage_ReturnsFalse(string? encoded)
    {
        SaveCodec.TryDecode(encoded, out var decoded).ShouldBeFalse();
        decoded.ShouldBeNull();
    }

    [Fact]
    public void BigDouble_SerializesAsCanonicalString() =>
        JsonSerializer.Serialize(new BigDouble(1.5, 300)).ShouldBe("\"1.5e300\"");
}
