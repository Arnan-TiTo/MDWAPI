using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MDWAPI.Common;

public static class JsonExt
{
    public static string? GetString(this JsonElement e, string prop)
        => e.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()
         : e.TryGetProperty(prop, out v) && v.ValueKind == JsonValueKind.Number ? v.ToString()
         : null;

    public static long? GetLong(this JsonElement e, string prop)
        => e.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt64()
         : long.TryParse(e.GetString(prop), out var l) ? l : null;

    public static decimal? GetDecimal(this JsonElement e, string prop)
        => e.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDecimal()
         : decimal.TryParse(e.GetString(prop), out var d) ? d : null;

    public static bool? GetBool(this JsonElement e, string prop)
        => e.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.True ? true
         : v.ValueKind == JsonValueKind.False ? false
         : bool.TryParse(e.GetString(prop), out var b) ? b : null;

    public static DateTimeOffset? FromUnixSeconds(long? sec)
        => sec is null ? null : DateTimeOffset.FromUnixTimeSeconds(sec.Value).ToUniversalTime();

    public static byte[] Sha256(string s)
        => SHA256.HashData(Encoding.UTF8.GetBytes(s));
}
