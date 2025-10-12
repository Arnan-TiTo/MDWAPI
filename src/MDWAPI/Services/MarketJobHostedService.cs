using System.Net.Http.Headers;
using System.Globalization;
using MDWAPI.Data;
using MDWAPI.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MDWAPI.Services;

public class MarketJobHostedService : BackgroundService
{
    private readonly ILogger<MarketJobHostedService> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHttpClientFactory _httpFactory;
    private readonly IConfiguration _cfg;
    private readonly IAuthTokenProvider _tokenProvider;

    public MarketJobHostedService(
        ILogger<MarketJobHostedService> logger,
        IServiceScopeFactory scopeFactory,
        IHttpClientFactory httpFactory,
        IConfiguration cfg,
        IAuthTokenProvider tokenProvider)
    {
        _logger = logger;
        _scopeFactory = scopeFactory;
        _httpFactory = httpFactory;
        _cfg = cfg;
        _tokenProvider = tokenProvider;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var pollCfg = _cfg["Jobs:PollInterval"];
        var interval = TimeSpan.FromSeconds(15);
        if (!string.IsNullOrWhiteSpace(pollCfg) && TimeSpan.TryParse(pollCfg, out var ts))
            interval = ts < TimeSpan.FromSeconds(5) ? TimeSpan.FromSeconds(5) : ts;

        _logger.LogInformation("MarketJobHostedService started. PollInterval={interval}", interval);

        var timer = new PeriodicTimer(interval);

        try { await RunOnceAsync(interval, stoppingToken); }
        catch (Exception ex) { _logger.LogError(ex, "Initial run failed"); }

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try { await RunOnceAsync(interval, stoppingToken); }
            catch (Exception ex) { _logger.LogError(ex, "MarketJob run error"); }
        }
    }

    private async Task RunOnceAsync(TimeSpan pollInterval, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var nowUtc = DateTime.UtcNow;
        var nowBkk = ToBangkok(nowUtc);

        var jobs = await db.Misc
            .Where(m => m.Type == "MarketJob" && m.IsActive)
            .OrderBy(m => m.Id)
            .ToListAsync(ct);

        foreach (var j in jobs)
        {
            try
            {
                if (!IsDue(j, nowBkk, pollInterval, db)) continue;

                var path = (j.Value2 ?? "").Trim();
                if (string.IsNullOrWhiteSpace(path)) continue;

                var baseQs = (j.Value3 ?? "").Trim().TrimStart('?');

                // สร้างหน้าต่างเวลาแบบ backfill + chunk + overlap (ทั้งหมดใน BKK time)
                var windows = BuildWindows(nowBkk, j.Value4, j.Value5);
                if (windows.Count == 0) continue;

                var client = _httpFactory.CreateClient("OrdersApi");
                var bearer = await _tokenProvider.GetBearerAsync(ct);
                if (!string.IsNullOrWhiteSpace(bearer))
                {
                    client.DefaultRequestHeaders.Remove("Authorization");
                    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
                }

                long finalTo = 0;
                foreach (var w in windows)
                {
                    var finalQs = MergeQuery(baseQs, w.FromEpoch, w.ToEpoch);

                    using var resp = await client.PostAsync(path + "?" + finalQs, content: null, ct);
                    var body = await resp.Content.ReadAsStringAsync(ct);

                    _logger.LogInformation(
                        "Job [{id}:{name}] {status} POST {path}?{qs} | windowUTC {fromUtc}->{toUtc} | len={len}",
                        j.Id, j.Name, (int)resp.StatusCode, path, finalQs,
                        DateTimeOffset.FromUnixTimeSeconds(w.FromEpoch).UtcDateTime,
                        DateTimeOffset.FromUnixTimeSeconds(w.ToEpoch).UtcDateTime,
                        body?.Length ?? 0);

                    finalTo = w.ToEpoch;
                }

                // อัปเดต timestamp และ watermark ถ้ามี remember
                var behavior = ParseBehavior(j.Value4);
                j.UpdatedAt = DateTime.UtcNow;
                if (behavior.Remember && finalTo > 0)
                    j.Value5 = finalTo.ToString(CultureInfo.InvariantCulture);

                await db.SaveChangesAsync(ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Job {id}:{name} failed", j.Id, j.Name);
            }
        }
    }

    // ===== Scheduling =====

    private static bool IsDue(Misc job, DateTime nowBkk, TimeSpan pollInterval, AppDbContext db)
    {
        var schedule = job.Value1?.Trim();
        if (string.IsNullOrWhiteSpace(schedule)) return false;

        var lastBkk = ToBangkok(job.UpdatedAt);
        var windowStart = lastBkk;
        var windowEnd = nowBkk + TimeSpan.FromSeconds(2);

        if (schedule.StartsWith("cron:", StringComparison.OrdinalIgnoreCase))
        {
            var name = schedule["cron:".Length..].Trim();
            if (string.IsNullOrWhiteSpace(name)) return false;

            var expr = db.Misc
                .Where(m => m.Type == "cronjob" && m.Name == name && m.IsActive)
                .Select(m => m.Value1)
                .FirstOrDefault();

            if (string.IsNullOrWhiteSpace(expr)) return false;
            return CronIsDue(expr!.Trim(), windowStart, windowEnd);
        }

        if (LooksLikeCron(schedule))
            return CronIsDue(schedule, windowStart, windowEnd);

        if (schedule.StartsWith("every:", StringComparison.OrdinalIgnoreCase))
        {
            var span = ParseSpan(schedule["every:".Length..]);
            return (nowBkk - lastBkk) >= span;
        }

        var times = schedule.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (times.Length > 0)
        {
            var days = new[] { nowBkk.Date.AddDays(-1), nowBkk.Date };
            foreach (var d in days)
                foreach (var t in times)
                {
                    if (!TimeSpan.TryParse(t, out var ts)) continue;
                    var due = d + ts;
                    if (due > windowStart && due <= windowEnd) return true;
                }
        }

        return false;
    }

    private static bool LooksLikeCron(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return false;
        var parts = s.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 5) return false;

        foreach (var p in parts)
            foreach (var ch in p)
                if (!(char.IsDigit(ch) || ch == '*' || ch == '/' || ch == '-' || ch == ','))
                    return false;

        return true;
    }

    private static bool CronIsDue(string expr, DateTime windowStartBkk, DateTime windowEndBkk)
    {
        if (windowEndBkk <= windowStartBkk) return false;

        var parts = expr.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 5) return false;

        var minutes = ParseCronField(parts[0], 0, 59);
        var hours = ParseCronField(parts[1], 0, 23);
        var dom = ParseCronField(parts[2], 1, 31);
        var mon = ParseCronField(parts[3], 1, 12);
        var dow = ParseCronField(parts[4], 0, 7); // 0/7 = Sun

        var t = new DateTime(windowStartBkk.Year, windowStartBkk.Month, windowStartBkk.Day, windowStartBkk.Hour, windowStartBkk.Minute, 0);
        if (t <= windowStartBkk) t = t.AddMinutes(1);

        while (t <= windowEndBkk)
        {
            int m = t.Minute, h = t.Hour, month = t.Month, day = t.Day, dayOfWeek = (int)t.DayOfWeek;
            bool dowMatch = dow.Contains(dayOfWeek) || (dayOfWeek == 0 && dow.Contains(7));
            if (minutes.Contains(m) && hours.Contains(h) && dom.Contains(day) && mon.Contains(month) && dowMatch)
                return true;
            t = t.AddMinutes(1);
        }
        return false;
    }

    private static HashSet<int> ParseCronField(string field, int min, int maxInclusive)
    {
        var set = new HashSet<int>();
        if (field == "*") { for (int i = min; i <= maxInclusive; i++) set.Add(i); return set; }

        foreach (var token in field.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (token.Contains('/'))
            {
                var sp = token.Split('/', 2);
                var rangePart = sp[0];
                var step = int.TryParse(sp[1], out var st) && st > 0 ? st : 1;

                int start = min, end = maxInclusive;
                if (rangePart != "*")
                {
                    var rs = rangePart.Split('-', 2);
                    if (rs.Length == 2 && int.TryParse(rs[0], out var a) && int.TryParse(rs[1], out var b))
                    { start = Math.Max(min, Math.Min(a, b)); end = Math.Min(maxInclusive, Math.Max(a, b)); }
                    else if (int.TryParse(rangePart, out var single))
                    { start = Math.Max(min, Math.Min(single, maxInclusive)); end = start; }
                }
                for (int i = start; i <= end; i += step) set.Add(i);
            }
            else if (tokenContainsDash(token: token, out var a2, out var b2))
            {
                int start = Math.Max(min, Math.Min(a2, b2));
                int end = Math.Min(maxInclusive, Math.Max(a2, b2));
                for (int i = start; i <= end; i++) set.Add(i);
            }
            else if (token == "*")
            {
                for (int i = min; i <= maxInclusive; i++) set.Add(i);
            }
            else if (int.TryParse(token, out var v))
            {
                if (v >= min && v <= maxInclusive) set.Add(v);
            }
        }

        return set.Count > 0 ? set : new HashSet<int>(Enumerable.Range(min, maxInclusive - min + 1));

        static bool tokenContainsDash(string token, out int a, out int b)
        {
            a = b = 0;
            var rs = token.Split('-', 2);
            if (rs.Length == 2 && int.TryParse(rs[0], out a) && int.TryParse(rs[1], out b))
                return true;
            return false;
        }
    }

    // ===== Windows / Behavior =====

    private record Behavior(TimeSpan Overlap, TimeSpan Chunk, bool Remember, DateTimeOffset? Backfill);

    private static Behavior ParseBehavior(string? value4)
    {
        var overlap = TimeSpan.FromMinutes(10);
        var chunk = (TimeSpan.FromDays(15) - TimeSpan.FromSeconds(1)); // 14d 23:59:59
        var remember = false;
        DateTimeOffset? backfill = null;

        var spec = (value4 ?? "").Trim();
        if (string.IsNullOrEmpty(spec)) return new Behavior(overlap, chunk, remember, backfill);

        foreach (var rawToken in spec.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var token = rawToken.Trim();

            if (token.Equals("remember", StringComparison.OrdinalIgnoreCase))
            {
                remember = true; continue;
            }

            if (token.StartsWith("chunk=", StringComparison.OrdinalIgnoreCase))
            {
                var v = token["chunk=".Length..].Trim();
                if (v.EndsWith("d", true, CultureInfo.InvariantCulture) && int.TryParse(v[..^1], out var dd))
                    chunk = TimeSpan.FromDays(dd);
                else if (v.EndsWith("h", true, CultureInfo.InvariantCulture) && int.TryParse(v[..^1], out var hh))
                    chunk = TimeSpan.FromHours(hh);
                else if (v.EndsWith("m", true, CultureInfo.InvariantCulture) && int.TryParse(v[..^1], out var mm))
                    chunk = TimeSpan.FromMinutes(mm);
                continue;
            }

            if (token.StartsWith("backfill=", StringComparison.OrdinalIgnoreCase))
            {
                var v = token["backfill=".Length..].Trim();
                if (long.TryParse(v, out var epoch) && epoch > 0)
                    backfill = DateTimeOffset.FromUnixTimeSeconds(epoch);
                else if (DateTimeOffset.TryParse(v, out var dt))
                    backfill = dt;
                continue;
            }

            // overlap literal: -10m / 10m / 2h / ...
            var t = token;
            if (t.StartsWith("-", StringComparison.Ordinal)) t = t[1..];
            var span = ParseSpan(t);
            overlap = span;
        }

        return new Behavior(overlap, chunk, remember, backfill);
    }

    private sealed class Window { public long FromEpoch; public long ToEpoch; }

    /// <summary>
    /// คำนวณช่วงเวลาใน "Bangkok time" ให้เสร็จ แล้วค่อยแปลงเป็น Unix epoch ด้วย offset BKK
    /// ถ้ามี watermark (Value5) → เริ่มที่ (watermark - overlap)
    /// ถ้าไม่มี → เริ่มที่ backfill (หรือย้อนหลัง 90 วัน) แล้วตัดเป็น chunk
    /// </summary>
    private static List<Window> BuildWindows(DateTime nowBkk, string? value4, string? value5)
    {
        var behavior = ParseBehavior(value4);
        var tz = GetBangkokTz();

        // ตีความ watermark (เป็น epoch UTC) → แปลงเป็น BKK
        bool hasWm = long.TryParse((value5 ?? "").Trim(), out var wmEpoch) && wmEpoch > 0;
        DateTime startBkk;

        if (hasWm)
        {
            var wmUtc = DateTimeOffset.FromUnixTimeSeconds(wmEpoch); // UTC moment
            var wmBkk = TimeZoneInfo.ConvertTime(wmUtc, tz);         // -> BKK
            startBkk = wmBkk.DateTime.Add(-behavior.Overlap);
        }
        else
        {
            if (behavior.Backfill.HasValue)
            {
                var bfBkk = TimeZoneInfo.ConvertTime(behavior.Backfill.Value, tz);
                startBkk = bfBkk.DateTime;
            }
            else
            {
                startBkk = nowBkk.AddDays(-90);
            }
        }

        var endBkk = nowBkk;
        if (endBkk <= startBkk) return new List<Window>();

        var list = new List<Window>();
        var cursor = startBkk;

        while (cursor < endBkk)
        {
            var next = cursor.Add(behavior.Chunk);
            if (next > endBkk) next = endBkk;

            list.Add(new Window
            {
                FromEpoch = ToEpochFromBangkok(cursor),
                ToEpoch = ToEpochFromBangkok(next)
            });

            cursor = next;
        }

        return list;
    }

    private static string MergeQuery(string baseQs, long fromEpoch, long toEpoch)
    {
        var dict = ToQueryDict(baseQs);
        dict["timeFrom"] = fromEpoch.ToString(CultureInfo.InvariantCulture);
        dict["timeTo"] = toEpoch.ToString(CultureInfo.InvariantCulture);
        return string.Join("&", dict.Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"));
    }

    private static Dictionary<string, string> ToQueryDict(string qs)
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(qs)) return d;
        foreach (var pair in qs.Split('&', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var i = pair.IndexOf('=');
            if (i < 0) { d[pair] = ""; continue; }
            var k = Uri.UnescapeDataString(pair[..i]);
            var v = Uri.UnescapeDataString(pair[(i + 1)..]);
            d[k] = v;
        }
        return d;
    }

    // ===== Utilities (เวลา/ไทม์โซน) =====

    private static TimeSpan ParseSpan(string s)
    {
        s = s.Trim();
        if (s.StartsWith("-", StringComparison.Ordinal)) s = s[1..];
        if (s.EndsWith("h", StringComparison.OrdinalIgnoreCase) && int.TryParse(s[..^1], out var hh))
            return TimeSpan.FromHours(hh);
        if (s.EndsWith("m", StringComparison.OrdinalIgnoreCase) && int.TryParse(s[..^1], out var mm))
            return TimeSpan.FromMinutes(mm);
        if (s.EndsWith("s", StringComparison.OrdinalIgnoreCase) && int.TryParse(s[..^1], out var ss))
            return TimeSpan.FromSeconds(ss);
        if (int.TryParse(s, out var mins)) return TimeSpan.FromMinutes(mins);
        return TimeSpan.FromMinutes(10);
    }

    private static DateTime ToBangkok(DateTime utc)
    {
        try
        {
            var tz = TimeZoneInfo.FindSystemTimeZoneById("Asia/Bangkok");
            return TimeZoneInfo.ConvertTimeFromUtc(utc, tz);
        }
        catch
        {
#if WINDOWS
            var tz = TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time");
            return TimeZoneInfo.ConvertTimeFromUtc(utc, tz);
#else
            return utc.AddHours(7);
#endif
        }
    }

    private static TimeZoneInfo GetBangkokTz()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Asia/Bangkok");
        }
        catch
        {
#if WINDOWS
            return TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time");
#else
            // Bangkok = UTC+7 (no DST)
            return TimeZoneInfo.CreateCustomTimeZone("Asia/Bangkok_Fallback", TimeSpan.FromHours(7), "Bangkok", "Bangkok");
#endif
        }
    }

    /// <summary>
    /// แปลง DateTime (ตีความว่าเป็น Bangkok time) → Unix epoch seconds อย่างถูกต้อง
    /// </summary>
    private static long ToEpochFromBangkok(DateTime localBkkTime)
    {
        var tz = GetBangkokTz();
        // บังคับเป็น "Unspecified" เพื่อให้ offset มาจากโซน BKK เสมอ
        var unspecified = DateTime.SpecifyKind(localBkkTime, DateTimeKind.Unspecified);
        var dto = new DateTimeOffset(unspecified, tz.GetUtcOffset(unspecified));
        return dto.ToUnixTimeSeconds();
    }

    private static string Trunc(string s, int max) => string.IsNullOrEmpty(s) || s.Length <= max ? s : s[..max] + "...";
}
