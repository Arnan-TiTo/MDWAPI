namespace MDWAPI.Common;

public enum Platform
{
    Shopee,
    Lazada,
    TikTok
}

public static class PlatformExt
{
    // คืนค่า "shopee" | "lazada" | "tiktok"
    public static string ToChannelString(this Platform pf)
        => pf switch
        {
            Platform.Shopee => "shopee",
            Platform.Lazada => "lazada",
            Platform.TikTok => "tiktok",
            _ => throw new ArgumentOutOfRangeException(nameof(pf), pf, null)
        };
}
