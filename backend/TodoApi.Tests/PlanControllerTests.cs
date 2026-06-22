using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Json;
using TodoApi.Data;
using TodoApi.Services;

namespace TodoApi.Tests;

public class PlanControllerTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly string _dbName = "PlanTestDb_" + Guid.NewGuid();

    public PlanControllerTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                var descriptor = services.SingleOrDefault(
                    d => d.ServiceType == typeof(DbContextOptions<AppDbContext>));
                if (descriptor != null) services.Remove(descriptor);
                services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(_dbName));
            });
        });
    }

    const string Yaml = """
        tasky:
           - id: a
             title: První
             odhad: 1h
             muzu_zacit: 2026-06-01
             dependencies:
           - id: b
             title: Druhý
             odhad: 1h
             muzu_zacit: 2026-06-01
             dependencies:
                - a
        """;

    [Fact]
    public async Task GetPlan_ReturnsOk_WithNowAndTimeline()
    {
        // seed via ManifestService (no file IO) so the full DB → plan path is exercised
        using (var scope = _factory.Services.CreateScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<ManifestService>();
            await svc.ApplyToDbAsync(svc.Deserialize(Yaml));
        }

        var client = _factory.CreateClient();
        var res = await client.GetAsync("/api/plan?horizon=3m");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var plan = await res.Content.ReadFromJsonAsync<PlanResult>();
        Assert.NotNull(plan);
        Assert.Equal("3m", plan!.Horizon);
        Assert.Equal(2, plan.Timeline.Count);
        Assert.NotNull(plan.Now);               // 'a' is actionable
        Assert.Equal("a", plan.Now!.Slug);
        Assert.Contains(plan.Blocked, n => n.Slug == "b"); // 'b' waits on 'a'
    }

    [Fact]
    public async Task GetPlan_ReturnsEmptyPlan_WhenNoTasks()
    {
        var client = _factory.CreateClient();
        var res = await client.GetAsync("/api/plan");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var plan = await res.Content.ReadFromJsonAsync<PlanResult>();
        Assert.NotNull(plan);
        Assert.Empty(plan!.Timeline);
    }
}
