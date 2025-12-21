using System.Security.Cryptography;
using System.Text;

namespace MDWAPI.Helpers
{
    public static class TikTokSign
    {
        public static string BuildSign(string appSecret, string path, IDictionary<string, string?> query)
        {
            if (string.IsNullOrWhiteSpace(appSecret)) throw new ArgumentException("appSecret required");
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("path required");
            if (query is null) throw new ArgumentNullException(nameof(query));

            var dict = query
                .Where(kv => !string.Equals(kv.Key, "sign", StringComparison.OrdinalIgnoreCase))
                .OrderBy(kv => kv.Key, StringComparer.Ordinal)
                .ToList();

            var sb = new StringBuilder();
            sb.Append(path);
            foreach (var kv in dict)
            {
                sb.Append(kv.Key);
                sb.Append(kv.Value ?? "");
            }

            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(appSecret));
            var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()));
            return Convert.ToHexString(hash).ToLowerInvariant();
        }
    }
}
