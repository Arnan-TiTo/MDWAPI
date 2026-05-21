using System.Text.Json;
using MDWAPI.Dtos;
using MDWAPI.Repos;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace MDWAPI.Services
{
    public class ChannelTokenResolver
    {
        private readonly IConfiguration _cfg;
        private readonly IChannelTokenRepo _repo;
        private readonly ILogger<ChannelTokenResolver> _log;

        public ChannelTokenResolver(
            IConfiguration cfg,
            IChannelTokenRepo repo,
            ILogger<ChannelTokenResolver> log)
        {
            _cfg = cfg;
            _repo = repo;
            _log = log;
        }

        public string HostFor(string channel, string environment)
        {
            var ch = (channel ?? "").ToLowerInvariant();
            var env = string.IsNullOrWhiteSpace(environment) ? "prod" : environment.ToLowerInvariant();

            return (ch, env) switch
            {
                ("shopee", "sandbox") => "https://openplatform.sandbox.test-stable.shopee.sg",
                ("shopee", _) => "https://partner.shopeemobile.com",

                ("lazada", "sandbox") => "https://api.lazada.test",
                ("lazada", _) => "https://api.lazada.com",

                ("tiktok", "sandbox") => "https://sandbox-open-api.tiktokglobalshop.com",
                ("tiktok", _) => "https://open-api.tiktokglobalshop.com",

                _ => throw new InvalidOperationException($"Unknown channel/env: {channel}/{environment}")
            };
        }

        public async Task<(string accessToken, string environment, long? partnerId, string? appKey)> GetAccessTokenAsync(
            string channel,
            string environment,
            long? partnerId,
            string? appKey,
            long? accountIdBig,
            string? accountIdStr,
            CancellationToken ct)
        {
            // ค้นแบบตรงๆ ก่อน
            var row = await _repo.GetValidAsync(channel, environment, partnerId, appKey, accountIdBig, accountIdStr, ct);

            // ถ้ายังไม่เจอ ลอง cross-field: string ↔ long
            if (row is null)
            {
                if (string.IsNullOrWhiteSpace(accountIdStr) && accountIdBig.HasValue)
                {
                    row = await _repo.GetValidAsync(channel, environment, partnerId, appKey, null, accountIdBig.Value.ToString(), ct);
                }
                else if (!string.IsNullOrWhiteSpace(accountIdStr) && long.TryParse(accountIdStr, out var asBig))
                {
                    row = await _repo.GetValidAsync(channel, environment, partnerId, appKey, asBig, null, ct);
                }
            }

            if (row is null)
                throw new InvalidOperationException($"No valid token for {channel}/{environment} (accountIdBig={accountIdBig}, accountIdStr={accountIdStr}).");

            return (row.AccessToken, row.Environment, row.PartnerId, row.AppKey);
        }

        /// <summary>
        /// อ่าน app_secret จาก ChannelTokens.ExtraJson ของ “บัญชีเดียวกัน” ก่อน
        /// ถ้าไม่เจอ ค่อย fallback ไปที่ Partners (ผ่าน config bridge)
        /// </summary>
        public async Task<string> ResolveAppSecretAsync(
            string channel,
            string environment,
            int partnersId,
            string appKey,
            string? accountIdStr,
            CancellationToken ct)
        {
            // 1) หา row เดียวกับบัญชีนี้ก่อน (ใช้ accountIdStr)
            if (!string.IsNullOrWhiteSpace(accountIdStr))
            {
                var row = await _repo.GetValidAsync(channel, environment, null, appKey, null, accountIdStr, ct);
                if (row is null && long.TryParse(accountIdStr, out var asBig))
                {
                    row = await _repo.GetValidAsync(channel, environment, null, appKey, asBig, null, ct);
                }

                if (row != null && !string.IsNullOrWhiteSpace(row.ExtraJson))
                {
                    try
                    {
                        using var doc = JsonDocument.Parse(NormalizeJson(row.ExtraJson));
                        if (doc.RootElement.TryGetProperty("app_secret", out var s) && s.ValueKind == JsonValueKind.String)
                            return s.GetString()!;
                    }
                    catch { /* ignore json error */ }
                }
            }

            // 2) fallback: หาแถวใดๆ ของ appKey เดียวกัน (โดยไม่ผูกกับบัญชี)
            //    ใช้เคล็ดลับ: ลองสุ่มหาด้วย accountIdStr = "#" (ค่าที่ computed column Norm ของคุณใช้)
            var anyRow = await _repo.GetValidAsync(channel, environment, null, appKey, null, "#", ct);
            if (anyRow != null && !string.IsNullOrWhiteSpace(anyRow.ExtraJson))
            {
                try
                {
                    using var doc = JsonDocument.Parse(NormalizeJson(anyRow.ExtraJson));
                    if (doc.RootElement.TryGetProperty("app_secret", out var s) && s.ValueKind == JsonValueKind.String)
                        return s.GetString()!;
                }
                catch { /* ignore */ }
            }

            // 3) สุดท้าย: ลองอ่านจาก config bridge (ถ้าแมพ Partners ลง config ไว้)
            var partnerKey = _cfg[$"Partners:{partnersId}:PartnerKey"];
            if (!string.IsNullOrWhiteSpace(partnerKey))
                return partnerKey!;

            throw new InvalidOperationException("TikTok app_secret not found. Store it in ChannelTokens.ExtraJson {\"app_secret\":\"...\"} หรือ Partners.PartnerKey");
        }

        /// <summary>
        /// Overload เพื่อ backward-compat (โค้ดเดิมที่ยังไม่ส่ง accountIdStr)
        /// </summary>
        public Task<string> ResolveAppSecretAsync(
            string channel,
            string environment,
            int partnersId,
            string appKey,
            CancellationToken ct)
        {
            // ส่งต่อไปเมธอดใหม่ด้วย accountIdStr = null
            return ResolveAppSecretAsync(channel, environment, partnersId, appKey, null, ct);
        }

        /// <summary>อ่าน shop_cipher จาก ChannelTokens.ExtraJson ของบัญชีนี้ (รองรับ cross-field: AccountIdStr ↔ AccountIdBig)</summary>
public async Task<string?> ResolveShopCipherAsync(
    string channel,
    string environment,
    string appKey,
    string accountIdStr,
    CancellationToken ct)
{
    ChannelTokenDtos? row = null;

    // 1) ตรงตัว (AccountIdStr)
    row = await _repo.GetValidAsync(channel, environment, null, appKey, null, accountIdStr, ct);

    // 2) cross-field (AccountIdBig)
    if (row is null && long.TryParse(accountIdStr, out var asBig))
        row = await _repo.GetValidAsync(channel, environment, null, appKey, asBig, null, ct);

    // 3) fallback: ไม่จำเป็นต้อง token ยัง valid ก็อ่าน cipher ได้ -> ใช้ latest
    if (row is null)
        row = await _repo.GetLatestForTikTokShopAsync(accountIdStr, ct);

    return TryReadCipher(row?.ExtraJson);
}

/// <summary>
/// บันทึก shop_cipher ลง ExtraJson ของ token row ล่าสุด (พยายาม update row เดิมด้วย Upsert)
/// </summary>
public async Task<bool> UpsertShopCipherAsync(
    string channel,
    string environment,
    string? appKey,
    string accountIdStr,
    string shopCipher,
    CancellationToken ct)
{
    if (string.IsNullOrWhiteSpace(shopCipher)) return false;

    ChannelTokenDtos? row = null;

    // พยายามหา row ที่ตรง env/appKey ก่อน
    if (!string.IsNullOrWhiteSpace(appKey))
    {
        row = await _repo.GetValidAsync(channel, environment, null, appKey, null, accountIdStr, ct);

        if (row is null && long.TryParse(accountIdStr, out var asBig))
            row = await _repo.GetValidAsync(channel, environment, null, appKey, asBig, null, ct);
    }

    // ถ้ายังไม่เจอ ให้ใช้ latest (อาจต่าง env/appKey แต่ cipher ใช้ได้เหมือนกัน)
    row ??= await _repo.GetLatestForTikTokShopAsync(accountIdStr, ct);

    if (row is null) return false;

    row.ExtraJson = MergeExtraJson(row.ExtraJson, new Dictionary<string, string?>
    {
        ["shop_cipher"] = shopCipher,
        ["cipher"] = shopCipher
    });

    // ถ้า row เดิมเก็บเป็น AccountIdBig อย่างเดียว ให้เติม AccountIdStr เพื่อให้หาเจอง่ายขึ้นในอนาคต
    if (string.IsNullOrWhiteSpace(row.AccountIdStr))
        row.AccountIdStr = accountIdStr;

    // ถ้าไม่มี appKey ให้ใส่ (กัน upsert key หลุด)
    if (string.IsNullOrWhiteSpace(row.AppKey) && !string.IsNullOrWhiteSpace(appKey))
        row.AppKey = appKey;

    await _repo.UpsertAsync(row, ct);
    return true;
}

private static string? TryReadCipher(string? extraJson)
{
    if (string.IsNullOrWhiteSpace(extraJson)) return null;
    try
    {
        using var doc = JsonDocument.Parse(NormalizeJson(extraJson));
        var root = doc.RootElement;

        if (root.TryGetProperty("shop_cipher", out var s1) && s1.ValueKind == JsonValueKind.String)
            return s1.GetString();

        if (root.TryGetProperty("cipher", out var s2) && s2.ValueKind == JsonValueKind.String)
            return s2.GetString();

        if (root.TryGetProperty("shopCipher", out var s3) && s3.ValueKind == JsonValueKind.String)
            return s3.GetString();
    }
    catch { }

    return null;
}

private static string MergeExtraJson(string? existingJson, IDictionary<string, string?> patch)
{
    var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

    if (!string.IsNullOrWhiteSpace(existingJson))
    {
        try
        {
            using var doc = JsonDocument.Parse(NormalizeJson(existingJson));
            if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var p in doc.RootElement.EnumerateObject())
                    dict[p.Name] = p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() : p.Value.ToString();
            }
        }
        catch { }
    }

    foreach (var kv in patch)
        if (!string.IsNullOrWhiteSpace(kv.Value))
            dict[kv.Key] = kv.Value;

    return JsonSerializer.Serialize(dict);
}

        /// <summary>
        /// ทำความสะอาด JSON string (กันตัวอักษรควบคุมแปลก ๆ ทำให้ parse พัง)
        /// </summary>
        private static string NormalizeJson(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return raw;
            return raw.Replace("¶", "")
                      .Replace("\r\n", "\n")
                      .Replace("\r", "\n");
        }
    }
}
