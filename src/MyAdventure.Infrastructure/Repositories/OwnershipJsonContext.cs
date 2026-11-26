using System.Text.Json.Serialization;

namespace MyAdventure.Infrastructure.Repositories;

[JsonSerializable(typeof(Dictionary<string, long>))]
[JsonSerializable(typeof(Dictionary<string, bool>))]
internal sealed partial class OwnershipJsonContext : JsonSerializerContext
{
}
