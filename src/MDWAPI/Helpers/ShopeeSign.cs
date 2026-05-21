using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace MDWAPI.Helpers;

public enum ShopeeKeyMode
{
    RawString,          // ใช้ partnerKey ทั้งก้อน (รวม shp/shpk) เป็น UTF8 key
    StripHexToBytes,    // ตัด shp/shpk แล้วตีความส่วนที่เหลือเป็น HEX -> bytes -> ใช้เป็น key
    StripPrefixAscii    // ตัด shp/shpk แล้วใช้ส่วนที่เหลือเป็น UTF8 key (ไม่แปลง HEX)
}

public static class ShopeeSign
{
    private static readonly Regex ShpPrefix = new(@"^(shpk|shp)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex HexOnly = new(@"^[0-9a-fA-F]+$", RegexOptions.Compiled);

    private static byte[] GetKeyBytes(string partnerKey, ShopeeKeyMode mode)
    {
        if (string.IsNullOrWhiteSpace(partnerKey))
            throw new ArgumentException("partnerKey is required", nameof(partnerKey));

        switch (mode)
        {
            case ShopeeKeyMode.RawString:
                return Encoding.UTF8.GetBytes(partnerKey);

            case ShopeeKeyMode.StripHexToBytes:
                {
                    var core = ShpPrefix.Replace(partnerKey, ""); 
                    if (!HexOnly.IsMatch(core))
                        throw new ArgumentException("PartnerKey (after stripping prefix) is not HEX for StripHexToBytes mode.");
                    if (core.Length % 2 != 0)
                        throw new ArgumentException("HEX length must be even.");
                    var bytes = new byte[core.Length / 2];
                    for (int i = 0; i < bytes.Length; i++)
                        bytes[i] = Convert.ToByte(core.Substring(i * 2, 2), 16);
                    return bytes;
                }

            case ShopeeKeyMode.StripPrefixAscii:
                {
                    var core = ShpPrefix.Replace(partnerKey, "");
                    return Encoding.UTF8.GetBytes(core);
                }

            default:
                throw new NotSupportedException($"Unknown ShopeeKeyMode: {mode}");
        }
    }

    public static string ComputeHexHmac(string content, string partnerKey, ShopeeKeyMode mode)
    {
        var keyBytes = GetKeyBytes(partnerKey, mode);
        using var h = new HMACSHA256(keyBytes);
        var hash = h.ComputeHash(Encoding.UTF8.GetBytes(content));
        var sb = new StringBuilder(hash.Length * 2);
        foreach (var b in hash) sb.Append(b.ToString("x2"));
        return sb.ToString();
    }

    // partner-level: base = partner_id + api_path + timestamp
    public static string BuildPartnerAuthSign(long partnerId, string partnerKey, string apiPath, long timestamp, ShopeeKeyMode mode)
    {
        var baseStr = $"{partnerId}{apiPath}{timestamp}";
        return ComputeHexHmac(baseStr, partnerKey, mode);
    }

    // shop-level: base = partner_id + api_path + timestamp + access_token + shop_id
    public static string BuildShopSign(long partnerId, string partnerKey, string apiPath, long timestamp, string accessToken, long shopId, ShopeeKeyMode mode)
    {
        var baseStr = $"{partnerId}{apiPath}{timestamp}{accessToken}{shopId}";
        return ComputeHexHmac(baseStr, partnerKey, mode);
    }
}
