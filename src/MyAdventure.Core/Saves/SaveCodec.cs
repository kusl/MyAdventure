using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MyAdventure.Core.Numerics;

namespace MyAdventure.Core.Saves;

public static class SaveCodec
{
    public const int CurrentVersion = 2;

    public static string Encode(GameSnapshot snapshot, DateTime exportedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var export = new SaveExport
        {
            Version = CurrentVersion,
            Cash = snapshot.Cash,
            Lifetime = snapshot.LifetimeEarnings,
            Angels = snapshot.AngelInvestors,
            Prestige = snapshot.PrestigeCount,
            Businesses = snapshot.Owned,
            Managers = snapshot.Managers,
            Timestamp = exportedAtUtc.ToString("o", CultureInfo.InvariantCulture),
        };

        return Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(export, SaveJsonContext.Default.SaveExport));
    }

    public static bool TryDecode(string? encoded, [NotNullWhen(true)] out GameSnapshot? snapshot)
    {
        snapshot = null;
        if (string.IsNullOrWhiteSpace(encoded)) return false;

        try
        {
            var json = Encoding.UTF8.GetString(Convert.FromBase64String(encoded.Trim()));
            if (JsonSerializer.Deserialize(json, SaveJsonContext.Default.SavePayload) is not { } payload) return false;

            snapshot = new GameSnapshot
            {
                Cash = payload.Cash,
                LifetimeEarnings = payload.Lifetime,
                AngelInvestors = payload.Angels,
                PrestigeCount = payload.Prestige,
                Owned = payload.Businesses ?? new Dictionary<string, long>(),
                Managers = payload.Managers ?? new Dictionary<string, bool>(),
            };
            return true;
        }
        catch (Exception ex) when (ex is FormatException or JsonException or NotSupportedException)
        {
            return false;
        }
    }
}

internal record SavePayload
{
    [JsonPropertyName("v")] public int Version { get; init; }
    [JsonPropertyName("cash")] public BigDouble Cash { get; init; }
    [JsonPropertyName("lifetime")] public BigDouble Lifetime { get; init; }
    [JsonPropertyName("angels")] public BigDouble Angels { get; init; }
    [JsonPropertyName("prestige")] public int Prestige { get; init; }
    [JsonPropertyName("businesses")] public IReadOnlyDictionary<string, long>? Businesses { get; init; }
    [JsonPropertyName("managers")] public IReadOnlyDictionary<string, bool>? Managers { get; init; }
}

internal sealed record SaveExport : SavePayload
{
    [JsonPropertyName("timestamp"), JsonPropertyOrder(1)] public required string Timestamp { get; init; }
}

[JsonSerializable(typeof(SavePayload))]
[JsonSerializable(typeof(SaveExport))]
internal sealed partial class SaveJsonContext : JsonSerializerContext
{
}
