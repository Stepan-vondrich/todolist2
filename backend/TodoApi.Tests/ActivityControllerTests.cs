using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Net.Http.Json;
using TodoApi.Data;
using TodoApi.Models;

namespace TodoApi.Tests;

public class ActivityControllerTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public ActivityControllerTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    private (HttpClient client, string dbName) CreateClient()
    {
        var dbName = "TestDb_Activity_" + Guid.NewGuid();
        var client = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                var descriptor = services.SingleOrDefault(
                    d => d.ServiceType == typeof(DbContextOptions<AppDbContext>));
                if (descriptor != null) services.Remove(descriptor);
                services.AddDbContext<AppDbContext>(options =>
                    options.UseInMemoryDatabase(dbName));
            });
        }).CreateClient();
        return (client, dbName);
    }

    static DateTime Utc(int y, int m, int d) => new(y, m, d, 12, 0, 0, DateTimeKind.Utc);

    private async Task Seed(string dbName, Action<AppDbContext> seed)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName).Options;
        using var db = new AppDbContext(options);
        seed(db);
        await db.SaveChangesAsync();
    }

    private static async Task<List<int>> GetActivity(HttpClient client, string query) =>
        (await client.GetFromJsonAsync<List<int>>($"/api/activity?{query}"))!;

    [Fact]
    public async Task Activity_UsesLocalDate_NotUtc_ForBoundaryTimestamps()
    {
        // A task created late-evening local time can be "yesterday" in UTC when the
        // machine is ahead of UTC. The filter must match the LOCAL calendar date the
        // user sees in the UI (this is a desktop app running in the user's timezone),
        // otherwise "today" misses tasks created tonight.
        var (client, db) = CreateClient();

        // Pick a local instant near midnight, then store its UTC equivalent (mirrors how
        // the app persists DateTime.UtcNow). Skip when the machine runs at UTC, where
        // local and UTC dates always agree and there's nothing to distinguish.
        var localMidnightish = new DateTime(2026, 6, 20, 0, 30, 0, DateTimeKind.Local);
        var asUtc = localMidnightish.ToUniversalTime();
        if (DateOnly.FromDateTime(asUtc) == DateOnly.FromDateTime(localMidnightish))
            return; // running at/near UTC — boundary case not reproducible here

        await Seed(db, c => c.Todos.Add(
            new TodoItem { Id = 1, Title = "tonight", CreatedAt = asUtc }));

        var ids = await GetActivity(client, "from=2026-06-20&to=2026-06-20&types=created");

        Assert.Contains(1, ids);
    }

    [Fact]
    public async Task Activity_Created_MatchesTodoCreatedInRange()
    {
        var (client, db) = CreateClient();
        await Seed(db, c => c.Todos.AddRange(
            new TodoItem { Id = 1, Title = "in",  CreatedAt = Utc(2026, 6, 10) },
            new TodoItem { Id = 2, Title = "out", CreatedAt = Utc(2026, 5, 1) }));

        var ids = await GetActivity(client, "from=2026-06-01&to=2026-06-30&types=created");

        Assert.Contains(1, ids);
        Assert.DoesNotContain(2, ids);
    }

    [Fact]
    public async Task Activity_Modified_MatchesTodoWithNonCreateLogInRange()
    {
        var (client, db) = CreateClient();
        await Seed(db, c =>
        {
            c.Todos.AddRange(
                new TodoItem { Id = 1, Title = "edited",   CreatedAt = Utc(2026, 1, 1) },
                new TodoItem { Id = 2, Title = "untouched", CreatedAt = Utc(2026, 1, 1) });
            c.TaskLogs.Add(new TaskLog { TodoId = 1, EventType = "column_change", Timestamp = Utc(2026, 6, 15) });
        });

        var ids = await GetActivity(client, "from=2026-06-01&to=2026-06-30&types=modified");

        Assert.Contains(1, ids);
        Assert.DoesNotContain(2, ids);
    }

    [Fact]
    public async Task Activity_Modified_IgnoresCreateEvents()
    {
        var (client, db) = CreateClient();
        await Seed(db, c =>
        {
            c.Todos.Add(new TodoItem { Id = 1, Title = "t", CreatedAt = Utc(2026, 1, 1) });
            // Only a "create" log in range — must NOT count as a modification.
            c.TaskLogs.Add(new TaskLog { TodoId = 1, EventType = "create", Timestamp = Utc(2026, 6, 15) });
        });

        var ids = await GetActivity(client, "from=2026-06-01&to=2026-06-30&types=modified");

        Assert.DoesNotContain(1, ids);
    }

    [Fact]
    public async Task Activity_Commented_MatchesTodoWithCommentInRange()
    {
        var (client, db) = CreateClient();
        await Seed(db, c =>
        {
            c.Todos.AddRange(
                new TodoItem { Id = 1, Title = "commented", CreatedAt = Utc(2026, 1, 1) },
                new TodoItem { Id = 2, Title = "silent",    CreatedAt = Utc(2026, 1, 1) });
            c.Comments.Add(new Comment { TodoId = 1, Text = "hi", CreatedAt = Utc(2026, 6, 20) });
        });

        var ids = await GetActivity(client, "from=2026-06-01&to=2026-06-30&types=commented");

        Assert.Contains(1, ids);
        Assert.DoesNotContain(2, ids);
    }

    [Fact]
    public async Task Activity_TypesCombineWithOr()
    {
        var (client, db) = CreateClient();
        await Seed(db, c =>
        {
            c.Todos.AddRange(
                new TodoItem { Id = 1, Title = "created-in",  CreatedAt = Utc(2026, 6, 5) },
                new TodoItem { Id = 2, Title = "commented-in", CreatedAt = Utc(2026, 1, 1) },
                new TodoItem { Id = 3, Title = "nothing",      CreatedAt = Utc(2026, 1, 1) });
            c.Comments.Add(new Comment { TodoId = 2, Text = "x", CreatedAt = Utc(2026, 6, 9) });
        });

        var ids = await GetActivity(client, "from=2026-06-01&to=2026-06-30&types=created,commented");

        Assert.Contains(1, ids);
        Assert.Contains(2, ids);
        Assert.DoesNotContain(3, ids);
    }

    [Fact]
    public async Task Activity_RangeBoundsAreInclusive()
    {
        var (client, db) = CreateClient();
        await Seed(db, c => c.Todos.AddRange(
            new TodoItem { Id = 1, Title = "first-day", CreatedAt = Utc(2026, 6, 1) },
            new TodoItem { Id = 2, Title = "last-day",  CreatedAt = Utc(2026, 6, 30) },
            new TodoItem { Id = 3, Title = "day-after", CreatedAt = Utc(2026, 7, 1) }));

        var ids = await GetActivity(client, "from=2026-06-01&to=2026-06-30&types=created");

        Assert.Contains(1, ids);
        Assert.Contains(2, ids);
        Assert.DoesNotContain(3, ids);
    }

    [Fact]
    public async Task Activity_EmptyRange_ReturnsAllTodos()
    {
        var (client, db) = CreateClient();
        await Seed(db, c => c.Todos.AddRange(
            new TodoItem { Id = 1, Title = "a", CreatedAt = Utc(2026, 1, 1) },
            new TodoItem { Id = 2, Title = "b", CreatedAt = Utc(2026, 1, 1) }));

        var ids = await GetActivity(client, "types=created");

        Assert.Contains(1, ids);
        Assert.Contains(2, ids);
    }

    [Fact]
    public async Task Activity_OpenEndedFrom_MatchesEverythingFromThatDateOn()
    {
        var (client, db) = CreateClient();
        await Seed(db, c => c.Todos.AddRange(
            new TodoItem { Id = 1, Title = "before", CreatedAt = Utc(2026, 5, 30) },
            new TodoItem { Id = 2, Title = "after",  CreatedAt = Utc(2026, 6, 10) }));

        var ids = await GetActivity(client, "from=2026-06-01&types=created");

        Assert.DoesNotContain(1, ids);
        Assert.Contains(2, ids);
    }

    [Fact]
    public async Task Activity_OpenEndedTo_MatchesEverythingUpToThatDate()
    {
        var (client, db) = CreateClient();
        await Seed(db, c => c.Todos.AddRange(
            new TodoItem { Id = 1, Title = "early", CreatedAt = Utc(2026, 5, 30) },
            new TodoItem { Id = 2, Title = "late",  CreatedAt = Utc(2026, 6, 10) }));

        var ids = await GetActivity(client, "to=2026-06-01&types=created");

        Assert.Contains(1, ids);
        Assert.DoesNotContain(2, ids);
    }

    [Fact]
    public async Task Activity_NoTypes_DefaultsToAllActivityKinds()
    {
        var (client, db) = CreateClient();
        await Seed(db, c =>
        {
            c.Todos.AddRange(
                new TodoItem { Id = 1, Title = "comment-only", CreatedAt = Utc(2026, 1, 1) },
                new TodoItem { Id = 2, Title = "none",         CreatedAt = Utc(2026, 1, 1) });
            c.Comments.Add(new Comment { TodoId = 1, Text = "c", CreatedAt = Utc(2026, 6, 15) });
        });

        // No `types` param → treat as all kinds, so the comment alone qualifies todo 1.
        var ids = await GetActivity(client, "from=2026-06-01&to=2026-06-30");

        Assert.Contains(1, ids);
        Assert.DoesNotContain(2, ids);
    }
}
