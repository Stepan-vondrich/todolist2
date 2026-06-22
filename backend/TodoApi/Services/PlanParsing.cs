using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using TodoApi.Models;

namespace TodoApi.Services;

/// <summary>Pure parsing helpers shared by the controller (DB → engine input) and tests.</summary>
public static partial class PlanParsing
{
    // Czech weekday abbreviations in week order.
    static readonly string[] DayOrder = { "po", "ut", "st", "ct", "pa", "so", "ne" };
    static readonly DayOfWeek[] DayMap =
    {
        DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday,
        DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday,
    };

    public static DateTime HorizonEnd(DateTime now, string? raw)
    {
        var m = HorizonRe().Match((raw ?? "3m").Trim().ToLowerInvariant());
        if (!m.Success) return now.AddMonths(3);
        int n = int.Parse(m.Groups[1].Value);
        return m.Groups[2].Value switch
        {
            "d" => now.AddDays(n),
            "w" => now.AddDays(7 * n),
            "m" => now.AddMonths(n),
            "y" or "r" => now.AddYears(n),
            _ => now.AddMonths(n),
        };
    }

    public static int EstimateMinutes(string? odhad)
    {
        if (string.IsNullOrWhiteSpace(odhad)) return 0;
        var s = odhad.Trim().ToLowerInvariant();
        int total = 0; bool matched = false;
        foreach (Match m in EstimateRe().Matches(s))
        {
            matched = true;
            int v = int.Parse(m.Groups[1].Value);
            total += m.Groups[2].Value == "h" ? v * 60 : v;
        }
        if (matched) return total;
        return int.TryParse(s, out var bare) ? bare : 0; // bare number = minutes
    }

    public static PlanSettings FromNastaveni(NastaveniDto? n)
    {
        n ??= ManifestService.DefaultNastaveni();
        var s = new PlanSettings { HorizonRaw = n.HorizontPlanovani ?? "3m" };

        if (n.PracovniDoba is not null)
            foreach (var (key, val) in n.PracovniDoba)
            {
                var range = ParseTimeRange(val);
                if (range is null) continue;
                foreach (var dow in ExpandDays(key))
                {
                    if (!s.WorkHours.TryGetValue(dow, out var list))
                        s.WorkHours[dow] = list = new();
                    list.Add(range.Value);
                }
            }

        if (n.OknaDne is not null)
            foreach (var (key, val) in n.OknaDne)
            {
                var range = ParseTimeRange(val);
                if (range is not null) s.Windows[key] = range.Value;
            }

        if (n.ReakceLidi is not null)
            foreach (var (key, val) in n.ReakceLidi)
            {
                var d = ParseDuration(val);
                if (d is not null) s.ReakceLidi[key] = d.Value;
            }

        return s;
    }

    public static PevnyCasInfo? ParsePevnyCas(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var s = raw.Trim();
        var parts = s.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (parts.Length == 2 &&
            DateTime.TryParse(parts[0], CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            var r = ParseTimeRange(parts[1]);
            if (r is null) return null;
            return new PevnyCasInfo { Start = date.Date + r.Value.from, End = date.Date + r.Value.to };
        }

        var t = ParseTimeRange(s);
        if (t is null) return null;
        return new PevnyCasInfo { TimeFrom = t.Value.from, TimeTo = t.Value.to };
    }

    public static PlanTask ToPlanTask(TodoItem todo, TaskManifest m) => new()
    {
        TodoId = todo.Id,
        Slug = m.Slug,
        Title = todo.Title,
        Status = todo.Status,
        IsCompleted = todo.IsCompleted,
        EstimateMinutes = EstimateMinutes(m.Odhad),
        Dependencies = DeList(m.Dependencies),
        MuzuZacit = m.MuzuZacit,
        Deadline = m.Deadline,
        JenVPraci = m.JenVPraci,
        Kdy = DeList(m.Kdy),
        MuzeBezetS = DeList(m.MuzeBezetS),
        CekaNaCloveka = DeCeka(m.CekaNaCloveka),
        PevnyCas = ParsePevnyCas(m.PevnyCas),
        Periodicita = m.Periodicita,
        Priority = todo.Priority,
        SortOrder = todo.SortOrder,
        CreatedAt = todo.CreatedAt,
    };

    // ── small parsers ─────────────────────────────────────────────────────────────
    static IEnumerable<DayOfWeek> ExpandDays(string key)
    {
        key = key.Trim().ToLowerInvariant();
        if (key.Contains('-'))
        {
            var ends = key.Split('-', 2);
            int a = Array.IndexOf(DayOrder, ends[0].Trim());
            int b = Array.IndexOf(DayOrder, ends[1].Trim());
            if (a >= 0 && b >= 0)
                for (int i = a; i <= b; i++) yield return DayMap[i];
            yield break;
        }
        foreach (var tok in key.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            int idx = Array.IndexOf(DayOrder, tok);
            if (idx >= 0) yield return DayMap[idx];
        }
    }

    static (TimeSpan from, TimeSpan to)? ParseTimeRange(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var parts = raw.Trim().Trim('"').Split('-', 2);
        if (parts.Length != 2) return null;
        if (TimeSpan.TryParse(parts[0].Trim(), out var a) && TimeSpan.TryParse(parts[1].Trim(), out var b) && b > a)
            return (a, b);
        return null;
    }

    static TimeSpan? ParseDuration(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var m = DurationRe().Match(raw.Trim().ToLowerInvariant());
        if (!m.Success) return null;
        int n = int.Parse(m.Groups[1].Value);
        return m.Groups[2].Value switch
        {
            "h" => TimeSpan.FromHours(n),
            "d" => TimeSpan.FromDays(n),
            "w" => TimeSpan.FromDays(7 * n),
            _ => TimeSpan.FromMinutes(n),
        };
    }

    static List<string> DeList(string json)
    {
        try { return JsonSerializer.Deserialize<List<string>>(string.IsNullOrWhiteSpace(json) ? "[]" : json) ?? new(); }
        catch { return new(); }
    }

    static CekaNaClovekaInfo? DeCeka(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            var dto = JsonSerializer.Deserialize<CekaNaClovekaDto>(json,
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            if (dto is null) return null;
            return new CekaNaClovekaInfo { Kdo = dto.Kdo ?? "", Reakce = dto.Reakce ?? "normalne" };
        }
        catch { return null; }
    }

    [GeneratedRegex(@"^(\d+)\s*([dwmyr])$")]
    private static partial Regex HorizonRe();

    [GeneratedRegex(@"(\d+)\s*(h|m)")]
    private static partial Regex EstimateRe();

    [GeneratedRegex(@"^(\d+)\s*([mhdw])$")]
    private static partial Regex DurationRe();
}
