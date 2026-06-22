using TodoApi.Services;

namespace TodoApi.Tests;

public class SchedulerServiceTests
{
    // Mon–Fri 09:00–17:00; windows rano/dopo/odpo/vecer; people reactions.
    static PlanSettings DefaultSettings() => PlanParsing.FromNastaveni(ManifestService.DefaultNastaveni());

    // A fixed Monday 09:00 wall-clock "now".
    static readonly DateTime Now = new(2026, 6, 1, 9, 0, 0); // 2026-06-01 is a Monday

    static PlanTask Task(string slug, int minutes, string[]? deps = null, DateTime? deadline = null,
        DateTime? muzuZacit = null, bool jenVPraci = false, string[]? kdy = null, string priority = "")
        => new()
        {
            Slug = slug,
            Title = slug,
            EstimateMinutes = minutes,
            Dependencies = deps?.ToList() ?? new(),
            Deadline = deadline,
            MuzuZacit = muzuZacit,
            JenVPraci = jenVPraci,
            Kdy = kdy?.ToList() ?? new(),
            Priority = priority,
            CreatedAt = new DateTime(2026, 5, 1),
        };

    static PlanResult Run(params PlanTask[] tasks) =>
        new SchedulerService().Simulate(new SchedulerInput
        {
            Tasks = tasks.ToList(),
            Settings = DefaultSettings(),
            Now = Now,
        });

    [Fact]
    public void Dependency_Chain_PlacesInOrder()
    {
        var r = Run(
            Task("b", 60, deps: new[] { "a" }),
            Task("a", 60));

        var a = r.Timeline.First(n => n.Slug == "a");
        var b = r.Timeline.First(n => n.Slug == "b");
        Assert.True(b.PredictedStart >= a.PredictedFinish, "b must start after a finishes");
    }

    [Fact]
    public void Now_IsReadyTask_DependentIsBlocked()
    {
        var r = Run(
            Task("a", 60),
            Task("b", 60, deps: new[] { "a" }));

        Assert.NotNull(r.Now);
        Assert.Equal("a", r.Now!.Slug);
        Assert.Contains(r.Blocked, n => n.Slug == "b");
        Assert.Equal("task", r.Blocked.First(n => n.Slug == "b").BlockedBy!.Kind);
    }

    [Fact]
    public void Slack_AtRiskTaskOrderedFirst_OverSlackRichOne()
    {
        // urgent: 4h work, deadline in ~2h of worktime → negative slack
        var urgent = Task("urgent", 240, deadline: new DateTime(2026, 6, 1, 11, 0, 0));
        var relaxed = Task("relaxed", 60, deadline: new DateTime(2026, 7, 1, 0, 0, 0));
        var r = Run(relaxed, urgent);

        // urgent has lower (negative) slack → it is the "now" action AND flagged at-risk
        Assert.Equal("urgent", r.Now!.Slug);
        Assert.Contains(r.AtRisk, n => n.Slug == "urgent");
    }

    [Fact]
    public void Priority_BreaksTies_WhenSlackEqual()
    {
        var p2 = Task("p2", 60, priority: "2");
        var p1 = Task("p1", 60, priority: "1");
        var r = Run(p2, p1);
        Assert.Equal("p1", r.Now!.Slug); // lower priority number wins the tie
    }

    [Fact]
    public void JenVPraci_NotPlacedOnWeekend()
    {
        // Friday 16:00 now, 4h work task that is work-hours-only → must spill to Monday, never Sat/Sun
        var fridayNow = new DateTime(2026, 6, 5, 16, 0, 0); // Friday
        var task = Task("w", 240, jenVPraci: true);
        var r = new SchedulerService().Simulate(new SchedulerInput
        {
            Tasks = new() { task }, Settings = DefaultSettings(), Now = fridayNow,
        });
        var n = r.Timeline.First();
        Assert.NotEqual(DayOfWeek.Saturday, n.PredictedFinish.DayOfWeek);
        Assert.NotEqual(DayOfWeek.Sunday, n.PredictedFinish.DayOfWeek);
    }

    [Fact]
    public void MuzuZacit_GatesStart()
    {
        var start = new DateTime(2026, 6, 10, 0, 0, 0);
        var r = Run(Task("later", 60, muzuZacit: start));
        var n = r.Timeline.First();
        Assert.True(n.PredictedStart >= start);
        Assert.Contains(r.Blocked, b => b.Slug == "later" && b.BlockedBy!.Kind == "start");
    }

    [Fact]
    public void FixedTime_IsAnchored_AndCarvesOutTime()
    {
        var fixedTask = Task("schuzka", 60);
        fixedTask.PevnyCas = new PevnyCasInfo
        {
            Start = new DateTime(2026, 6, 1, 10, 0, 0),
            End = new DateTime(2026, 6, 1, 11, 0, 0),
        };
        var other = Task("prace", 120); // would otherwise run 09:00–11:00
        var r = Run(fixedTask, other);

        var s = r.Timeline.First(n => n.Slug == "schuzka");
        Assert.Equal(new DateTime(2026, 6, 1, 10, 0, 0), s.PredictedStart);
        Assert.Equal(new DateTime(2026, 6, 1, 11, 0, 0), s.PredictedFinish);

        // 'prace' (120m from 09:00) is split around the 10–11 meeting → 09–10 + 11–12,
        // so the carve-out pushes its finish to 12:00 instead of 11:00.
        var p = r.Timeline.First(n => n.Slug == "prace");
        Assert.Equal(new DateTime(2026, 6, 1, 12, 0, 0), p.PredictedFinish);
    }

    [Fact]
    public void Kdy_PrefersWindow_WhenItFits()
    {
        // now Monday 09:00, prefer 'odpo' (14:00–18:00), 60m, no deadline → start at 14:00
        var r = Run(Task("t", 60, kdy: new[] { "odpo" }));
        var n = r.Timeline.First();
        Assert.Equal(new TimeSpan(14, 0, 0), n.PredictedStart.TimeOfDay);
        Assert.False(n.SoftWindowMissed);
    }

    [Fact]
    public void Kdy_RelaxesAndFlags_WhenWindowCannotMeetDeadline()
    {
        // prefer 'vecer' (18:00–23:00) but deadline today 12:00 → window can't make it → relax + flag
        var r = Run(Task("t", 60, kdy: new[] { "vecer" }, deadline: new DateTime(2026, 6, 1, 12, 0, 0)));
        var n = r.Timeline.First();
        Assert.True(n.SoftWindowMissed);
        Assert.Equal(new TimeSpan(9, 0, 0), n.PredictedStart.TimeOfDay); // placed now, not in vecer
    }

    [Fact]
    public void Determinism_SameInputSameOutput()
    {
        var r1 = Run(Task("a", 60), Task("b", 90, deps: new[] { "a" }));
        var r2 = Run(Task("a", 60), Task("b", 90, deps: new[] { "a" }));
        Assert.Equal(r1.Timeline.Select(n => n.Slug + n.PredictedStart.Ticks),
                     r2.Timeline.Select(n => n.Slug + n.PredictedStart.Ticks));
    }

    [Fact]
    public void DependencyCycle_EmitsAlert_DoesNotThrow()
    {
        var r = Run(
            Task("a", 60, deps: new[] { "b" }),
            Task("b", 60, deps: new[] { "a" }));
        Assert.Contains(r.Alerts, a => a.Type == "dependency_cycle");
    }

    [Fact]
    public void PeriodicTask_HasOnePendingOccurrence_RestAreTimelineOnly()
    {
        var daily = Task("daily", 30);
        daily.Periodicita = "denne";
        var r = Run(daily);

        int actionable = new[] { r.Now }.Count(n => n?.Slug == "daily")
            + r.Next.Count(n => n.Slug == "daily")
            + r.Blocked.Count(n => n.Slug == "daily");
        Assert.Equal(1, actionable); // only one pending occurrence surfaces
        Assert.True(r.Timeline.Count(n => n.Slug.StartsWith("daily")) > 5); // many future occurrences on the timeline
        Assert.Contains(r.Timeline, n => n.Slug == "daily" && n.IsOccurrence);
    }

    [Fact]
    public void PeriodicStuck_WhenOccurrenceDueButDependencyNotDone()
    {
        var prep = Task("prep", 60);                         // not done
        var weekly = Task("report", 30, deps: new[] { "prep" });
        weekly.Periodicita = "tydne";
        weekly.MuzuZacit = Now.Date;                          // first occurrence today
        var r = Run(prep, weekly);

        Assert.Contains(r.Alerts, a => a.Type == "periodic_stuck" && a.Slug == "report");
        Assert.Contains(r.Blocked, n => n.Slug == "report");
    }

    [Fact]
    public void MuzeBezetS_TasksMayOverlap_InsteadOfSerializing()
    {
        var a = Task("a", 120); a.MuzeBezetS = new() { "b" };
        var b = Task("b", 120); b.MuzeBezetS = new() { "a" };
        var r = Run(a, b);

        var na = r.Timeline.First(n => n.Slug == "a");
        var nb = r.Timeline.First(n => n.Slug == "b");
        Assert.Equal(na.PredictedStart, nb.PredictedStart); // share the same window, not queued
        Assert.Contains("a", nb.SharesWindowWith);
    }

    [Fact]
    public void NonSharing_TasksSerialize_OnSharedTimeline()
    {
        var r = Run(Task("a", 120), Task("b", 120));
        var na = r.Timeline.First(n => n.Slug == "a");
        var nb = r.Timeline.First(n => n.Slug == "b");
        Assert.True(nb.PredictedStart >= na.PredictedFinish); // one thing at a time
    }

    [Fact]
    public void DownstreamImpact_CountsTransitiveDependents()
    {
        var r = Run(
            Task("root", 30),
            Task("mid", 30, deps: new[] { "root" }),
            Task("leaf", 30, deps: new[] { "mid" }));
        var root = r.Timeline.First(n => n.Slug == "root");
        Assert.Equal(2, root.DownstreamImpactCount);
    }
}
