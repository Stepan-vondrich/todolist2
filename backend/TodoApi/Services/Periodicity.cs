namespace TodoApi.Services;

/// <summary>
/// Expands a periodicita token into calendar occurrence dates within a horizon.
/// Grammar: denne | tydne[:po,st,pa] | mesicne[:15|prvni-streda|posledni-patek] |
/// kvartalne | rocne | interval:14d|3m|2w
/// </summary>
public static class Periodicity
{
    static readonly Dictionary<string, DayOfWeek> Weekdays = new()
    {
        ["po"] = DayOfWeek.Monday, ["pondeli"] = DayOfWeek.Monday,
        ["ut"] = DayOfWeek.Tuesday, ["utery"] = DayOfWeek.Tuesday,
        ["st"] = DayOfWeek.Wednesday, ["streda"] = DayOfWeek.Wednesday,
        ["ct"] = DayOfWeek.Thursday, ["ctvrtek"] = DayOfWeek.Thursday,
        ["pa"] = DayOfWeek.Friday, ["patek"] = DayOfWeek.Friday,
        ["so"] = DayOfWeek.Saturday, ["sobota"] = DayOfWeek.Saturday,
        ["ne"] = DayOfWeek.Sunday, ["nedele"] = DayOfWeek.Sunday,
    };

    static readonly Dictionary<string, int> Ordinals = new()
    {
        ["prvni"] = 1, ["druhy"] = 2, ["druha"] = 2, ["treti"] = 3,
        ["ctvrty"] = 4, ["ctvrta"] = 4, ["paty"] = 5, ["posledni"] = -1,
    };

    public static bool IsPeriodic(string? token) => !string.IsNullOrWhiteSpace(token);

    public static List<DateTime> Occurrences(string? token, DateTime start, DateTime end)
    {
        var dates = new List<DateTime>();
        if (string.IsNullOrWhiteSpace(token)) return dates;

        start = start.Date;
        var endDate = end.Date;
        var t = token.Trim().ToLowerInvariant();
        var (kind, arg) = t.Contains(':') ? (t[..t.IndexOf(':')], t[(t.IndexOf(':') + 1)..]) : (t, "");

        switch (kind)
        {
            case "denne":
                for (var d = start; d <= endDate; d = d.AddDays(1)) dates.Add(d);
                break;

            case "tydne":
                if (string.IsNullOrEmpty(arg))
                    for (var d = start; d <= endDate; d = d.AddDays(7)) dates.Add(d);
                else
                {
                    var set = ParseWeekdays(arg);
                    for (var d = start; d <= endDate; d = d.AddDays(1))
                        if (set.Contains(d.DayOfWeek)) dates.Add(d);
                }
                break;

            case "mesicne":
                AddMonthly(dates, start, endDate, arg);
                break;

            case "kvartalne":
                for (var d = start; d <= endDate; d = d.AddMonths(3)) dates.Add(d);
                break;

            case "rocne":
                for (var d = start; d <= endDate; d = d.AddYears(1)) dates.Add(d);
                break;

            case "interval":
                AddInterval(dates, start, endDate, arg);
                break;
        }

        return dates.Where(d => d >= start && d <= endDate).Distinct().OrderBy(d => d).Take(1000).ToList();
    }

    static void AddMonthly(List<DateTime> dates, DateTime start, DateTime end, string arg)
    {
        if (string.IsNullOrEmpty(arg))
        {
            // same day-of-month as start (clamped to month length)
            int day = start.Day;
            for (var m = new DateTime(start.Year, start.Month, 1); m <= end; m = m.AddMonths(1))
            {
                var d = ClampDay(m.Year, m.Month, day);
                if (d >= start && d <= end) dates.Add(d);
            }
            return;
        }

        if (int.TryParse(arg, out var nth))
        {
            for (var m = new DateTime(start.Year, start.Month, 1); m <= end; m = m.AddMonths(1))
            {
                var d = ClampDay(m.Year, m.Month, nth);
                if (d >= start && d <= end) dates.Add(d);
            }
            return;
        }

        // "prvni-streda" / "posledni-patek"
        var parts = arg.Split('-', 2);
        if (parts.Length == 2 && Ordinals.TryGetValue(parts[0], out var ord) && Weekdays.TryGetValue(parts[1], out var wd))
            for (var m = new DateTime(start.Year, start.Month, 1); m <= end; m = m.AddMonths(1))
            {
                var d = NthWeekdayOfMonth(m.Year, m.Month, wd, ord);
                if (d is { } dd && dd >= start && dd <= end) dates.Add(dd);
            }
    }

    static void AddInterval(List<DateTime> dates, DateTime start, DateTime end, string arg)
    {
        if (arg.Length < 2 || !int.TryParse(arg[..^1], out var n) || n <= 0) return;
        var unit = arg[^1];
        for (var d = start; d <= end;)
        {
            dates.Add(d);
            d = unit switch
            {
                'd' => d.AddDays(n),
                'w' => d.AddDays(7 * n),
                'm' => d.AddMonths(n),
                'y' or 'r' => d.AddYears(n),
                _ => end.AddDays(1), // unknown unit → stop
            };
        }
    }

    static HashSet<DayOfWeek> ParseWeekdays(string arg) =>
        arg.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
           .Where(Weekdays.ContainsKey).Select(s => Weekdays[s]).ToHashSet();

    static DateTime ClampDay(int year, int month, int day)
    {
        int dim = DateTime.DaysInMonth(year, month);
        return new DateTime(year, month, Math.Clamp(day, 1, dim));
    }

    static DateTime? NthWeekdayOfMonth(int year, int month, DayOfWeek wd, int ordinal)
    {
        if (ordinal == -1) // last
        {
            var last = new DateTime(year, month, DateTime.DaysInMonth(year, month));
            while (last.DayOfWeek != wd) last = last.AddDays(-1);
            return last;
        }
        var d = new DateTime(year, month, 1);
        while (d.DayOfWeek != wd) d = d.AddDays(1);
        d = d.AddDays(7 * (ordinal - 1));
        return d.Month == month ? d : null;
    }
}
