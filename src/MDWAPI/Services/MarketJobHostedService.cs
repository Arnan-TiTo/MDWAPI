using System.Net.Http.Headers;
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
        // อ่าน PollInterval จาก config (ดีฟอลต์ 15s, ขั้นต่ำ 5s)
        var pollCfg = _cfg["Jobs:PollInterval"];
        var interval = TimeSpan.FromSeconds(15);
        if (!string.IsNullOrWhiteSpace(pollCfg) && TimeSpan.TryParse(pollCfg, out var ts))
            interval = ts < TimeSpan.FromSeconds(5) ? TimeSpan.FromSeconds(5) : ts;

        _logger.LogInformation("MarketJobHostedService started. PollInterval={interval}", interval);

        var timer = new PeriodicTimer(interval);

        // รันหนึ่งครั้งทันทีเมื่อสตาร์ท (optional)
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
                var (fromEpoch, toEpoch, remember) = BuildWindow(nowBkk, j.Value4, j.Value5);
                var finalQs = MergeQuery(baseQs, fromEpoch, toEpoch);

                var client = _httpFactory.CreateClient("OrdersApi");
                // Bearer: login /api/Auth/login (cache ใน AuthTokenProvider)
                var bearer = await _tokenProvider.GetBearerAsync(ct);
                if (!string.IsNullOrWhiteSpace(bearer))
                {
                    client.DefaultRequestHeaders.Remove("Authorization");
                    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
                }

                using var resp = await client.PostAsync(path + "?" + finalQs, content: null, ct);
                var body = await resp.Content.ReadAsStringAsync(ct);

                _logger.LogInformation("Job [{id}:{name}] {status} POST {path}?{qs} | {body}",
                    j.Id, j.Name, (int)resp.StatusCode, path, finalQs, Trunc(body, 400));

                // อัปเดต timestamp และ state (Value5 = lastTo) หาก remember
                j.UpdatedAt = DateTime.UtcNow;
                if (remember) j.Value5 = toEpoch.ToString();
                await db.SaveChangesAsync(ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Job {id}:{name} failed", j.Id, j.Name);
            }
        }
    }

    // ====== DUE CHECK (รองรับ every:, HH:mm[,..], และ cron:NAME) ======

    private static bool IsDue(Misc job, DateTime nowBkk, TimeSpan pollInterval, AppDbContext db)
    {
        var schedule = job.Value1?.Trim();
        if (string.IsNullOrWhiteSpace(schedule)) return false;

        var lastBkk = ToBangkok(job.UpdatedAt);
        var windowStart = lastBkk;                       // (lastRun .. now]  ป้องกันพลาดรอบ
        var windowEnd = nowBkk + TimeSpan.FromSeconds(2); // +slack เล็กน้อย

        // --- A) ถ้าเป็น cron:NAME → lookup expression จาก dbo.Misc (type=cronjob) ---
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

        // --- B) ถ้าดูเหมือน cron expression (5 ฟิลด์คั่นด้วยช่องว่าง) → ประมวลผลตรง ๆ ---
        if (LooksLikeCron(schedule))
        {
            return CronIsDue(schedule, windowStart, windowEnd);
        }

        // --- C) every:X (เช่น every:1m, every:30s, every:2h) ---
        if (schedule.StartsWith("every:", StringComparison.OrdinalIgnoreCase))
        {
            var span = ParseSpan(schedule["every:".Length..]);
            return (nowBkk - lastBkk) >= span;
        }

        // --- D) fixed times "HH:mm,HH:mm" ---
        var times = schedule.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (times.Length > 0)
        {
            var days = new[] { nowBkk.Date.AddDays(-1), nowBkk.Date };
            foreach (var d in days)
            {
                foreach (var t in times)
                {
                    if (!TimeSpan.TryParse(t, out var ts)) continue;
                    var due = d + ts;
                    if (due > windowStart && due <= windowEnd) return true;
                }
            }
        }

        return false;
    }

    private static bool LooksLikeCron(string s)
    {
        // แบบง่าย: มี 5 ฟิลด์คั่นด้วยช่องว่าง และแต่ละฟิลด์มีเฉพาะตัวเลข * , - / ?
        if (string.IsNullOrWhiteSpace(s)) return false;
        var parts = s.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 5) return false;

        foreach (var p in parts)
        {
            foreach (var ch in p)
            {
                if (!(char.IsDigit(ch) || ch == '*' || ch == '/' || ch == '-' || ch == ',')) return false;
            }
        }
        return true;
    }

    // ====== CRON SUPPORT (5 ฟิลด์: m h dom mon dow) ======
    // รองรับ: *, */n, a-b, a,b,c, ผสมกันได้; DOW: 0-6 (0/7 = Sunday)
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

        // iterate ทีละ 1 นาทีในช่วง window (ปกติมักไม่ยาวมาก)
        // (ถ้าต้องรองรับ window ยาวหลายวัน สามารถกระโดดข้ามด้วย heuristic เพิ่มทีหลังได้)
        var t = new DateTime(windowStartBkk.Year, windowStartBkk.Month, windowStartBkk.Day, windowStartBkk.Hour, windowStartBkk.Minute, 0);
        if (t <= windowStartBkk) t = t.AddMinutes(1);

        while (t <= windowEndBkk)
        {
            int m = t.Minute;
            int h = t.Hour;
            int month = t.Month;
            int day = t.Day;
            int dayOfWeek = (int)t.DayOfWeek; // 0=Sunday

            // DOW อนุญาต 0 หรือ 7 เป็น Sunday
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
        if (field == "*")
        {
            for (int i = min; i <= maxInclusive; i++) set.Add(i);
            return set;
        }

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
                    {
                        start = Math.Max(min, Math.Min(a, b));
                        end = Math.Min(maxInclusive, Math.Max(a, b));
                    }
                    else if (int.TryParse(rangePart, out var single))
                    {
                        start = Math.Max(min, Math.Min(single, maxInclusive));
                        end = start;
                    }
                }
                for (int i = start; i <= end; i += step) set.Add(i);
            }
            else if (token.Contains('-'))
            {
                var rs = token.Split('-', 2);
                if (int.TryParse(rs[0], out var a) && int.TryParse(rs[1], out var b))
                {
                    int start = Math.Max(min, Math.Min(a, b));
                    int end = Math.Min(maxInclusive, Math.Max(a, b));
                    for (int i = start; i <= end; i++) set.Add(i);
                }
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
    }

    // ====== WINDOW BUILDER (เหมือนเดิม) ======
    /// Value4 pattern:
    ///  - "-10m"           => window 10 นาทีล่าสุด
    ///  - "-10m;remember"  => และจำ lastTo ใส่ Value5
    private static (long fromEpoch, long toEpoch, bool remember) BuildWindow(DateTime nowBkk, string? value4, string? value5)
    {
        var remember = false;
        var toEpoch = ToEpoch(nowBkk);
        var fromEpoch = toEpoch - 600; // default 10m

        var spec = (value4 ?? "").Trim();
        if (!string.IsNullOrEmpty(spec))
        {
            if (spec.Contains(';'))
            {
                var parts = spec.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                spec = parts[0];
                remember = parts.Any(p => p.Equals("remember", StringComparison.OrdinalIgnoreCase));
            }
            var span = ParseSpan(spec);
            fromEpoch = ToEpoch(nowBkk - span);
            toEpoch = ToEpoch(nowBkk);
        }

        if (remember && long.TryParse((value5 ?? "").Trim(), out var lastTo) && lastTo > 0)
        {
            fromEpoch = lastTo + 1;
            toEpoch = ToEpoch(nowBkk);
        }

        return (fromEpoch, toEpoch, remember);
    }

    private static string MergeQuery(string baseQs, long fromEpoch, long toEpoch)
    {
        var dict = ToQueryDict(baseQs);
        dict["timeFrom"] = fromEpoch.ToString();
        dict["timeTo"] = toEpoch.ToString();
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

    private static long ToEpoch(DateTime localTime) => new DateTimeOffset(localTime).ToUnixTimeSeconds();

    private static string Trunc(string s, int max) => s.Length <= max ? s : s[..max] + "...";
}
