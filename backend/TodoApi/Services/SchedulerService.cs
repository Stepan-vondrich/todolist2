namespace TodoApi.Services;

/// <summary>
/// Pure, deterministic forward-simulation scheduler. No DB access — the controller
/// loads inputs and passes them in, which keeps the algorithm unit-testable.
///
/// The model: a single shared timeline (the user does one thing at a time). Tasks are
/// placed in priority order, each consuming real available minutes (work hours or the
/// day-windows), gated by dependencies / muzu_zacit / waiting-on-a-person. Fixed
/// appointments (pevny_cas) are carved out first and are immovable. All math is in
/// wall-clock time (Now is injected), so results are deterministic regardless of machine TZ.
/// </summary>
public class SchedulerService
{
    PlanSettings _s = new();
    DateTime _now;
    DateTime _horizonEnd;

    public PlanResult Simulate(SchedulerInput input)
    {
        _s = input.Settings;
        _now = input.Now;
        _horizonEnd = PlanParsing.HorizonEnd(_now, _s.HorizonRaw);

        var tasks = Expand(input.Tasks);
        var bySlug = tasks.GroupBy(t => t.Slug).ToDictionary(g => g.Key, g => g.First());
        var result = new PlanResult { ComputedAt = _now, Horizon = _s.HorizonRaw };

        // predicted finish per slug (done = now; fixed = its end; scheduled = computed)
        var finish = new Dictionary<string, DateTime>();
        var nodes = new Dictionary<string, PlanNode>();
        // busy intervals carry their owner so muze_bezet_s tasks can overlap each other.
        var busy = new List<(DateTime start, DateTime end, string owner)>();

        // symmetric "may share a window" map
        var shareWith = tasks.ToDictionary(t => t.Slug, _ => new HashSet<string>());
        foreach (var t in tasks)
            foreach (var other in t.MuzeBezetS)
            {
                if (shareWith.TryGetValue(t.Slug, out var a)) a.Add(other);
                if (shareWith.TryGetValue(other, out var b)) b.Add(t.Slug);
            }

        // 0) done tasks: finished "now" so dependents can proceed
        foreach (var t in tasks.Where(t => t.Done))
        {
            finish[t.Slug] = _now;
            nodes[t.Slug] = MakeNode(t, _now, _now, "done");
        }

        // 1) fixed-time appointments: anchor + carve out the slot (immovable)
        foreach (var t in tasks.Where(t => !t.Done && t.PevnyCas?.Start is not null && t.PevnyCas.End is not null))
        {
            var ps = t.PevnyCas!.Start!.Value;
            var pe = t.PevnyCas.End!.Value;
            finish[t.Slug] = pe;
            busy.Add((ps, pe, t.Slug));
            var node = MakeNode(t, ps, pe, "future");
            nodes[t.Slug] = node;
        }
        busy.Sort((a, b) => a.start.CompareTo(b.start));

        // 2) static slack (for ordering) — theoretical capacity to the deadline minus work ahead
        var staticSlack = tasks.ToDictionary(t => t.Slug, t => StaticSlack(t, bySlug));

        // 3) priority-based topological placement of the remaining (floating) tasks
        var toPlace = tasks.Where(t => !t.Done && !nodes.ContainsKey(t.Slug)).Select(t => t.Slug).ToHashSet();
        var placed = new HashSet<string>(nodes.Keys); // done + fixed already resolved

        while (toPlace.Count > 0)
        {
            var ready = toPlace
                .Where(sl => bySlug[sl].Dependencies.All(d => !bySlug.ContainsKey(d) || placed.Contains(d) || (bySlug.TryGetValue(d, out var dt) && dt.Done)))
                .ToList();

            bool brokeCycle = false;
            if (ready.Count == 0)
            {
                // remaining tasks form a dependency cycle — break it deterministically
                ready = toPlace.ToList();
                brokeCycle = true;
            }

            var slug = ready.OrderBy(sl => staticSlack[sl])
                            .ThenBy(sl => bySlug[sl].Deadline ?? DateTime.MaxValue)
                            .ThenBy(sl => PriorityKey(bySlug[sl].Priority))
                            .ThenBy(sl => bySlug[sl].SortOrder)
                            .ThenBy(sl => bySlug[sl].CreatedAt)
                            .ThenBy(sl => sl, StringComparer.Ordinal)
                            .First();

            if (brokeCycle)
                result.Alerts.Add(new PlanAlert
                {
                    Type = "dependency_cycle",
                    Message = $"Cyklická závislost u tasku '{slug}' — přerušeno, aby plán nezamrzl.",
                    Slug = slug,
                    TodoId = bySlug[slug].TodoId,
                });

            var t = bySlug[slug];
            var gate = GateFor(t, finish);
            // tasks allowed to share a window with T don't block it → exclude their busy time
            var share = shareWith.TryGetValue(slug, out var sw) ? sw : new HashSet<string>();
            var effectiveBusy = busy.Where(b => !share.Contains(b.owner))
                                    .Select(b => (b.start, b.end)).ToList();
            var placement = Place(t, gate, effectiveBusy);
            foreach (var iv in placement.alloc) busy.Add((iv.Item1, iv.Item2, slug));
            busy.Sort((a, b) => a.start.CompareTo(b.start));

            finish[slug] = placement.finish;
            var newNode = MakeNode(t, placement.start, placement.finish, "future");
            newNode.SoftWindowMissed = placement.softMissed;
            // record who it actually overlaps among its allowed share-partners
            newNode.SharesWindowWith = busy
                .Where(b => share.Contains(b.owner) && b.start < placement.finish && b.end > placement.start)
                .Select(b => b.owner).Distinct().ToList();
            nodes[slug] = newNode;

            toPlace.Remove(slug);
            placed.Add(slug);
        }

        // 4) downstream impact (transitive dependents)
        var dependents = BuildDependents(tasks);
        foreach (var n in nodes.Values)
            n.DownstreamImpactCount = CountDownstream(n.Slug, dependents);

        // 5) classify into buckets
        Classify(tasks, bySlug, nodes, staticSlack, result);

        result.Timeline = nodes.Values.OrderBy(n => n.PredictedStart).ToList();
        return result;
    }

    // ── periodicity expansion ─────────────────────────────────────────────────────
    // Each periodic task yields an active occurrence (keeps the slug, participates in the
    // actionable buckets) plus later occurrences (timeline-only). "Only one pending at a time"
    // is realized by excluding the later occurrences from now/blocked/at-risk.
    List<PlanTask> Expand(List<PlanTask> input)
    {
        var result = new List<PlanTask>();
        foreach (var t in input)
        {
            if (!Periodicity.IsPeriodic(t.Periodicita) || t.Done) { result.Add(t); continue; }

            var dates = Periodicity.Occurrences(t.Periodicita, t.MuzuZacit ?? _now, _horizonEnd);
            if (dates.Count == 0) { result.Add(t); continue; }

            var active = dates.FirstOrDefault(d => d >= _now.Date);
            if (active == default) active = dates[^1];

            var activeTask = Clone(t);
            activeTask.OccurrenceDate = active;
            activeTask.MuzuZacit = active;
            result.Add(activeTask);

            foreach (var d in dates.Where(d => d > active))
            {
                var ft = Clone(t);
                ft.Slug = $"{t.Slug}#{d:yyyyMMdd}";
                ft.OccurrenceDate = d;
                ft.MuzuZacit = d;
                ft.IsFutureOccurrence = true;
                result.Add(ft);
            }
        }
        return result;
    }

    static PlanTask Clone(PlanTask t) => new()
    {
        TodoId = t.TodoId, Slug = t.Slug, Title = t.Title, Status = t.Status,
        IsCompleted = t.IsCompleted, EstimateMinutes = t.EstimateMinutes,
        Dependencies = new List<string>(t.Dependencies), MuzuZacit = t.MuzuZacit, Deadline = t.Deadline,
        JenVPraci = t.JenVPraci, Kdy = new List<string>(t.Kdy), MuzeBezetS = new List<string>(t.MuzeBezetS),
        CekaNaCloveka = t.CekaNaCloveka, PevnyCas = t.PevnyCas, Periodicita = t.Periodicita,
        Priority = t.Priority, SortOrder = t.SortOrder, CreatedAt = t.CreatedAt,
    };

    // ── gating ───────────────────────────────────────────────────────────────────
    DateTime GateFor(PlanTask t, Dictionary<string, DateTime> finish)
    {
        var gate = _now;
        if (t.MuzuZacit is { } mz && mz > gate) gate = mz;
        foreach (var dep in t.Dependencies)
            if (finish.TryGetValue(dep, out var f) && f > gate) gate = f;
        if (t.CekaNaCloveka is { } c)
        {
            var wait = _s.ReakceLidi.TryGetValue(c.Reakce, out var w) ? w : TimeSpan.FromDays(3);
            var until = _now + wait;
            if (until > gate) gate = until;
        }
        return gate;
    }

    // ── placement: consume estimate minutes on the shared calendar ────────────────
    (DateTime start, DateTime finish, bool completed, bool softMissed, List<(DateTime, DateTime)> alloc)
        Place(PlanTask t, DateTime gate, List<(DateTime, DateTime)> busy)
    {
        if (t.Kdy.Count == 0)
        {
            var r = Consume(t, gate, busy, preferredOnly: false);
            return (r.start, r.finish, r.completed, false, r.alloc);
        }
        // soft windows: try preferred first, relax if it misses the deadline / horizon
        var pref = Consume(t, gate, busy, preferredOnly: true);
        bool prefOk = pref.completed && (t.Deadline is null || pref.finish <= t.Deadline);
        if (prefOk) return (pref.start, pref.finish, true, false, pref.alloc);

        var full = Consume(t, gate, busy, preferredOnly: false);
        return (full.start, full.finish, full.completed, true, full.alloc);
    }

    (DateTime start, DateTime finish, bool completed, List<(DateTime, DateTime)> alloc)
        Consume(PlanTask t, DateTime gate, List<(DateTime, DateTime)> busy, bool preferredOnly)
    {
        int remaining = t.EstimateMinutes;
        var alloc = new List<(DateTime, DateTime)>();
        DateTime? start = null;

        for (var day = gate.Date; day <= _horizonEnd.Date && remaining > 0; day = day.AddDays(1))
        {
            foreach (var iv in DayIntervals(day, t, preferredOnly))
            {
                var s = iv.from < gate ? gate : iv.from;
                var e = iv.to > _horizonEnd ? _horizonEnd : iv.to;
                if (s >= e) continue;

                foreach (var free in SubtractBusy(s, e, busy))
                {
                    if (remaining <= 0) break;
                    var freeMin = (free.Item2 - free.Item1).TotalMinutes;
                    if (freeMin <= 0) continue;
                    start ??= free.Item1;
                    if (freeMin >= remaining)
                    {
                        var end = free.Item1.AddMinutes(remaining);
                        alloc.Add((free.Item1, end));
                        return (start.Value, end, true, alloc);
                    }
                    alloc.Add(free);
                    remaining -= (int)Math.Round(freeMin);
                }
            }
        }
        var finish = alloc.Count > 0 ? alloc[^1].Item2 : gate;
        return (start ?? gate, finish, remaining <= 0, alloc);
    }

    // ── available time-of-day intervals for a task on a given day ─────────────────
    IEnumerable<(DateTime from, DateTime to)> DayIntervals(DateTime day, PlanTask t, bool preferredOnly)
    {
        List<(TimeSpan from, TimeSpan to)> baseIvs;
        if (t.JenVPraci)
            baseIvs = _s.WorkHours.TryGetValue(day.DayOfWeek, out var wh) ? wh : new();
        else
            baseIvs = Merge(_s.Windows.Values.ToList());

        if (preferredOnly && t.Kdy.Count > 0)
        {
            var named = t.Kdy.Where(k => _s.Windows.ContainsKey(k)).Select(k => _s.Windows[k]).ToList();
            baseIvs = t.JenVPraci ? Intersect(baseIvs, named) : Merge(named);
        }

        foreach (var (f, to) in Merge(baseIvs))
            yield return (day + f, day + to);
    }

    // ── slack / capacity ──────────────────────────────────────────────────────────
    double StaticSlack(PlanTask t, Dictionary<string, PlanTask> bySlug)
    {
        if (t.Deadline is null) return double.PositiveInfinity;
        double ahead = TransitivePredecessorMinutes(t, bySlug, new());
        double capacity = CapacityMinutes(_now, t.Deadline.Value, t);
        return capacity - (t.EstimateMinutes + ahead);
    }

    double TransitivePredecessorMinutes(PlanTask t, Dictionary<string, PlanTask> bySlug, HashSet<string> seen)
    {
        double sum = 0;
        foreach (var dep in t.Dependencies)
        {
            if (!seen.Add(dep) || !bySlug.TryGetValue(dep, out var d) || d.Done) continue;
            sum += d.EstimateMinutes + TransitivePredecessorMinutes(d, bySlug, seen);
        }
        return sum;
    }

    double CapacityMinutes(DateTime from, DateTime to, PlanTask t)
    {
        if (to <= from) return 0;
        double total = 0;
        for (var day = from.Date; day <= to.Date; day = day.AddDays(1))
            foreach (var iv in DayIntervals(day, t, preferredOnly: false))
            {
                var s = iv.from < from ? from : iv.from;
                var e = iv.to > to ? to : iv.to;
                if (e > s) total += (e - s).TotalMinutes;
            }
        return total;
    }

    // ── classification into now / next / blocked / at-risk ────────────────────────
    void Classify(List<PlanTask> tasks, Dictionary<string, PlanTask> bySlug,
        Dictionary<string, PlanNode> nodes, Dictionary<string, double> slack, PlanResult result)
    {
        var ready = new List<PlanTask>();
        var bottleneckSlugs = new HashSet<string>();

        foreach (var t in tasks.Where(t => !t.Done))
        {
            // Later occurrences of a periodic task live on the timeline only — never pile up
            // as separate actionable/blocked items ("only one pending occurrence at a time").
            if (t.IsFutureOccurrence) continue;

            var node = nodes[t.Slug];
            node.SlackMinutes = double.IsInfinity(slack[t.Slug]) ? double.MaxValue : slack[t.Slug];

            bool startFuture = t.MuzuZacit is { } mz && mz > _now;
            bool personPending = t.CekaNaCloveka is not null;
            var notDoneDep = t.Dependencies.FirstOrDefault(d => bySlug.TryGetValue(d, out var dt) && !dt.Done);
            bool depPending = notDoneDep is not null;
            bool atRisk = t.Deadline is { } dl && node.PredictedFinish > dl;

            bool blocked = startFuture || personPending || depPending;

            if (atRisk) { node.State = "at_risk"; result.AtRisk.Add(node); }

            if (blocked)
            {
                node.BlockedBy = startFuture
                    ? new BlockedByInfo { Kind = "start", Ref = t.Slug, Reason = $"start {t.MuzuZacit:dd.MM.}" }
                    : personPending
                        ? new BlockedByInfo { Kind = "person", Ref = t.CekaNaCloveka!.Kdo, Reason = $"čeká na {t.CekaNaCloveka!.Kdo}" }
                        : new BlockedByInfo { Kind = "task", Ref = notDoneDep!, Reason = $"čeká na '{TitleOf(notDoneDep!, bySlug)}'" };
                if (!atRisk) node.State = "blocked";
                result.Blocked.Add(node);
                if (depPending) bottleneckSlugs.Add(notDoneDep!);

                // dependencies are superior to periodicita: an occurrence whose calendar date
                // has arrived but whose deps aren't done does NOT wait silently — it is flagged.
                if (depPending && t.OccurrenceDate is { } od && od <= _now)
                    result.Alerts.Add(new PlanAlert
                    {
                        Type = "periodic_stuck",
                        Message = $"Opakovaný task '{t.Title}' měl proběhnout, ale čeká na '{TitleOf(notDoneDep!, bySlug)}'.",
                        Slug = t.Slug,
                        TodoId = t.TodoId,
                        DownstreamImpact = node.DownstreamImpactCount,
                    });
            }
            else
            {
                // not blocked → actionable now; at-risk tasks belong here too (they are the
                // most urgent thing to do), they just also carry the at_risk flag/bucket.
                ready.Add(t);
            }
        }

        // bottleneck alerts: a not-done dependency that is holding up downstream work
        foreach (var sl in bottleneckSlugs)
        {
            if (!nodes.TryGetValue(sl, out var dn)) continue;
            result.Alerts.Add(new PlanAlert
            {
                Type = "bottleneck",
                Message = $"'{TitleOf(sl, bySlug)}' drží {dn.DownstreamImpactCount} navazujících — úzké hrdlo.",
                Slug = sl,
                TodoId = bySlug[sl].TodoId,
                DownstreamImpact = dn.DownstreamImpactCount,
            });
        }

        var ordered = ready
            .OrderBy(t => slack[t.Slug])
            .ThenBy(t => nodes[t.Slug].PredictedStart)
            .ThenBy(t => PriorityKey(t.Priority))
            .ToList();

        for (int i = 0; i < ordered.Count; i++)
        {
            var node = nodes[ordered[i].Slug];
            if (i == 0) { node.State = "now"; result.Now = node; }
            else { node.State = "next"; if (result.Next.Count < 5) result.Next.Add(node); }
        }

        result.AtRisk = result.AtRisk.OrderBy(n => n.SlackMinutes).ToList();
    }

    // ── helpers ──────────────────────────────────────────────────────────────────
    PlanNode MakeNode(PlanTask t, DateTime start, DateTime finishAt, string state) => new()
    {
        TodoId = t.TodoId,
        Slug = t.Slug,
        Title = t.Title,
        Status = t.Status,
        PredictedStart = start,
        PredictedFinish = finishAt,
        Deadline = t.Deadline,
        State = state,
        IsOccurrence = t.OccurrenceDate is not null,
        OccurrenceDate = t.OccurrenceDate,
    };

    static string TitleOf(string slug, Dictionary<string, PlanTask> bySlug) =>
        bySlug.TryGetValue(slug, out var t) && !string.IsNullOrEmpty(t.Title) ? t.Title : slug;

    static Dictionary<string, List<string>> BuildDependents(List<PlanTask> tasks)
    {
        var d = tasks.ToDictionary(t => t.Slug, _ => new List<string>());
        foreach (var t in tasks)
        {
            if (t.IsFutureOccurrence) continue; // don't inflate impact with future recurrences
            foreach (var dep in t.Dependencies)
                if (d.ContainsKey(dep)) d[dep].Add(t.Slug);
        }
        return d;
    }

    static int CountDownstream(string slug, Dictionary<string, List<string>> dependents)
    {
        var seen = new HashSet<string>();
        var stack = new Stack<string>(dependents.TryGetValue(slug, out var ds) ? ds : new());
        while (stack.Count > 0)
        {
            var cur = stack.Pop();
            if (!seen.Add(cur)) continue;
            if (dependents.TryGetValue(cur, out var more))
                foreach (var m in more) stack.Push(m);
        }
        return seen.Count;
    }

    static int PriorityKey(string priority) =>
        int.TryParse(priority, out var n) ? n : int.MaxValue;

    // ── interval math (time-of-day and concrete) ──────────────────────────────────
    static List<(TimeSpan from, TimeSpan to)> Merge(List<(TimeSpan from, TimeSpan to)> ivs)
    {
        var sorted = ivs.Where(i => i.to > i.from).OrderBy(i => i.from).ToList();
        var merged = new List<(TimeSpan from, TimeSpan to)>();
        foreach (var iv in sorted)
        {
            if (merged.Count > 0 && iv.from <= merged[^1].to)
                merged[^1] = (merged[^1].from, iv.to > merged[^1].to ? iv.to : merged[^1].to);
            else merged.Add(iv);
        }
        return merged;
    }

    static List<(TimeSpan from, TimeSpan to)> Intersect(
        List<(TimeSpan from, TimeSpan to)> a, List<(TimeSpan from, TimeSpan to)> b)
    {
        var res = new List<(TimeSpan from, TimeSpan to)>();
        foreach (var x in Merge(a))
            foreach (var y in Merge(b))
            {
                var s = x.from > y.from ? x.from : y.from;
                var e = x.to < y.to ? x.to : y.to;
                if (e > s) res.Add((s, e));
            }
        return Merge(res);
    }

    static IEnumerable<(DateTime, DateTime)> SubtractBusy(DateTime s, DateTime e, List<(DateTime, DateTime)> busy)
    {
        var cursor = s;
        foreach (var (bs, be) in busy.Where(b => b.Item2 > s && b.Item1 < e).OrderBy(b => b.Item1))
        {
            if (bs > cursor) yield return (cursor, bs < e ? bs : e);
            if (be > cursor) cursor = be;
            if (cursor >= e) yield break;
        }
        if (cursor < e) yield return (cursor, e);
    }
}
