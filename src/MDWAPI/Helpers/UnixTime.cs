namespace MDWAPI.Helpers;

public static class UnixTime
{
    /// <summary>เวลาปัจจุบัน (UTC) เป็น Unix seconds</summary>
    public static long NowSeconds() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    /// <summary>แปลง DateTime(UTC) → Unix seconds</summary>
    public static long ToSeconds(DateTime utc)
        => new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)).ToUnixTimeSeconds();

    /// <summary>แปลง Unix seconds → DateTime(UTC)</summary>
    public static DateTime ToUtc(long unixSeconds)
        => DateTimeOffset.FromUnixTimeSeconds(unixSeconds).UtcDateTime;
}
