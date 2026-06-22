using TodoApi.Services;

namespace TodoApi.Tests;

public class PeriodicityTests
{
    static readonly DateTime Start = new(2026, 6, 1); // Monday
    static readonly DateTime End = new(2026, 8, 31);

    [Fact]
    public void Denne_GeneratesEveryDay()
    {
        var d = Periodicity.Occurrences("denne", Start, Start.AddDays(4));
        Assert.Equal(5, d.Count);
        Assert.Equal(Start, d[0]);
    }

    [Fact]
    public void TydneWithWeekdays_GeneratesOnlyListedDays()
    {
        var d = Periodicity.Occurrences("tydne:po,st,pa", Start, Start.AddDays(13)); // two weeks
        Assert.All(d, x => Assert.Contains(x.DayOfWeek,
            new[] { DayOfWeek.Monday, DayOfWeek.Wednesday, DayOfWeek.Friday }));
        Assert.Equal(6, d.Count); // 3 per week × 2 weeks
    }

    [Fact]
    public void MesicneDay15_GeneratesFifteenth()
    {
        var d = Periodicity.Occurrences("mesicne:15", Start, End);
        Assert.All(d, x => Assert.Equal(15, x.Day));
        Assert.Equal(3, d.Count); // Jun/Jul/Aug 15
    }

    [Fact]
    public void MesicnePosledniPatek_GeneratesLastFriday()
    {
        var d = Periodicity.Occurrences("mesicne:posledni-patek", new DateTime(2026, 6, 1), new DateTime(2026, 6, 30));
        Assert.Single(d);
        Assert.Equal(DayOfWeek.Friday, d[0].DayOfWeek);
        Assert.Equal(new DateTime(2026, 6, 26), d[0]); // last Friday of June 2026
    }

    [Fact]
    public void MesicnePrvniStreda_GeneratesFirstWednesday()
    {
        var d = Periodicity.Occurrences("mesicne:prvni-streda", new DateTime(2026, 6, 1), new DateTime(2026, 6, 30));
        Assert.Single(d);
        Assert.Equal(new DateTime(2026, 6, 3), d[0]); // first Wednesday of June 2026
    }

    [Fact]
    public void Interval14d_GeneratesEveryFortnight()
    {
        var d = Periodicity.Occurrences("interval:14d", Start, Start.AddDays(30));
        Assert.Equal(new[] { Start, Start.AddDays(14), Start.AddDays(28) }, d.ToArray());
    }

    [Fact]
    public void Kvartalne_GeneratesEveryThreeMonths()
    {
        var d = Periodicity.Occurrences("kvartalne", new DateTime(2026, 1, 1), new DateTime(2026, 12, 31));
        Assert.Equal(4, d.Count);
    }
}
