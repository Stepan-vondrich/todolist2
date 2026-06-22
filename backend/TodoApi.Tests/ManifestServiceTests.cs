using Microsoft.EntityFrameworkCore;
using TodoApi.Data;
using TodoApi.Services;

namespace TodoApi.Tests;

public class ManifestServiceTests
{
    static AppDbContext NewDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase("Manifest_" + Guid.NewGuid()).Options);

    const string SampleYaml = """
        nastaveni:
           horizont_planovani: 3m
           pracovni_doba:
              po-pa: "09:00-17:30"
        tasky:
           - id: male_ukoly
             title: Vyřešit úkoly malého rozsahu
             odhad: 2h
             muzu_zacit: 2026-06-02
             dependencies:
           - id: faktury_brezen
             title: Zpracovat faktury za březen
             odhad: 2h
             muzu_zacit: 2026-06-02
             deadline: 2026-06-05
             kdy: [dopo]
             jen_v_praci: true
             periodicita: tydne
             dependencies:
                - male_ukoly
        """;

    [Fact]
    public async Task ApplyToDb_CreatesTodoAndManifest_BySlug()
    {
        using var db = NewDb();
        var svc = new ManifestService(db);

        await svc.ApplyToDbAsync(svc.Deserialize(SampleYaml));

        Assert.Equal(2, await db.TaskManifests.CountAsync());
        Assert.Equal(2, await db.Todos.CountAsync());
        var faktury = await db.TaskManifests.FirstAsync(m => m.Slug == "faktury_brezen");
        Assert.Equal("2h", faktury.Odhad);
        Assert.True(faktury.JenVPraci);
        Assert.Equal("tydne", faktury.Periodicita);
        Assert.Contains("male_ukoly", faktury.Dependencies);
        Assert.Equal(DateTimeKind.Utc, faktury.Deadline!.Value.Kind);
    }

    [Fact]
    public async Task ApplyToDb_IsIdempotent_UpdatesInPlaceBySlug()
    {
        using var db = NewDb();
        var svc = new ManifestService(db);

        await svc.ApplyToDbAsync(svc.Deserialize(SampleYaml));
        await svc.ApplyToDbAsync(svc.Deserialize(SampleYaml)); // apply again

        Assert.Equal(2, await db.TaskManifests.CountAsync());
        Assert.Equal(2, await db.Todos.CountAsync());
    }

    [Fact]
    public async Task RoundTrip_PreservesTasksAndSnakeCaseKeys()
    {
        using var db = NewDb();
        var svc = new ManifestService(db);
        await svc.ApplyToDbAsync(svc.Deserialize(SampleYaml));

        var yaml = svc.Serialize(await svc.BuildFromDbAsync());

        Assert.Contains("muzu_zacit", yaml);
        Assert.Contains("jen_v_praci", yaml);
        Assert.Contains("faktury_brezen", yaml);

        // deserialize the emitted yaml again — should still hold both tasks
        var dto = svc.Deserialize(yaml);
        Assert.Equal(2, dto.Tasky.Count);
    }

    [Fact]
    public async Task ApplyToDb_Throws_WhenRequiredFieldMissing()
    {
        using var db = NewDb();
        var svc = new ManifestService(db);
        var bad = """
            tasky:
               - id: x
                 title: X
                 muzu_zacit: 2026-06-02
                 dependencies:
            """; // missing odhad
        await Assert.ThrowsAsync<ManifestValidationException>(() =>
            svc.ApplyToDbAsync(svc.Deserialize(bad)));
    }

    [Fact]
    public async Task ApplyToDb_Throws_OnUnknownDependency()
    {
        using var db = NewDb();
        var svc = new ManifestService(db);
        var bad = """
            tasky:
               - id: x
                 title: X
                 odhad: 1h
                 muzu_zacit: 2026-06-02
                 dependencies:
                    - neexistuje
            """;
        await Assert.ThrowsAsync<ManifestValidationException>(() =>
            svc.ApplyToDbAsync(svc.Deserialize(bad)));
    }

    [Fact]
    public async Task LoadSettings_ReturnsDefaults_WhenUnset()
    {
        using var db = NewDb();
        var svc = new ManifestService(db);
        var data = await svc.LoadSettingsAsync();
        Assert.Equal("3m", data.Nastaveni.HorizontPlanovani);
        Assert.NotNull(data.Nastaveni.OknaDne);
    }
}
