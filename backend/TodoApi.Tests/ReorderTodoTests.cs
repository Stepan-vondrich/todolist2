using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Json;
using TodoApi.Data;
using TodoApi.Models;

namespace TodoApi.Tests;

public class ReorderTodoTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public ReorderTodoTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    // Each test gets its own in-memory DB so reorder writes never leak between tests.
    private (HttpClient client, string dbName) CreateClient()
    {
        var dbName = "TestDb_Reorder_" + Guid.NewGuid();
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

    // Seed directly through the same in-memory DB the client uses, so we control
    // SortOrder/ParentId precisely instead of going through the create endpoint.
    private async Task Seed(string dbName, params TodoItem[] items)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        using var db = new AppDbContext(options);
        db.Todos.AddRange(items);
        await db.SaveChangesAsync();
    }

    private record ReorderDto(int TargetId, string Position);
    private record TodoResponse(int Id, string Title, int? ParentId, int SortOrder);

    private static async Task<List<TodoResponse>> GetAll(HttpClient client) =>
        (await client.GetFromJsonAsync<List<TodoResponse>>("/api/todos"))!;

    [Fact]
    public async Task Reorder_ReturnsNotFound_WhenMovedTodoMissing()
    {
        var (client, _) = CreateClient();
        var resp = await client.PostAsJsonAsync("/api/todos/999/reorder", new ReorderDto(1, "before"));
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task Reorder_ReturnsNotFound_WhenTargetMissing()
    {
        var (client, db) = CreateClient();
        await Seed(db, new TodoItem { Id = 1, Title = "A", ParentId = null, SortOrder = 0 });
        var resp = await client.PostAsJsonAsync("/api/todos/1/reorder", new ReorderDto(999, "before"));
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task Reorder_Before_PlacesMovedTodoImmediatelyBeforeTarget_AsSibling()
    {
        var (client, db) = CreateClient();
        // Roots: A(0), B(1), C(2). Move C before A.
        await Seed(db,
            new TodoItem { Id = 1, Title = "A", ParentId = null, SortOrder = 0 },
            new TodoItem { Id = 2, Title = "B", ParentId = null, SortOrder = 1 },
            new TodoItem { Id = 3, Title = "C", ParentId = null, SortOrder = 2 });

        var resp = await client.PostAsJsonAsync("/api/todos/3/reorder", new ReorderDto(1, "before"));
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var roots = (await GetAll(client))
            .Where(t => t.ParentId == null).OrderBy(t => t.SortOrder).Select(t => t.Title).ToList();
        Assert.Equal(new[] { "C", "A", "B" }, roots);
    }

    [Fact]
    public async Task Reorder_After_PlacesMovedTodoImmediatelyAfterTarget_AsSibling()
    {
        var (client, db) = CreateClient();
        // Roots: A(0), B(1), C(2). Move A after B.
        await Seed(db,
            new TodoItem { Id = 1, Title = "A", ParentId = null, SortOrder = 0 },
            new TodoItem { Id = 2, Title = "B", ParentId = null, SortOrder = 1 },
            new TodoItem { Id = 3, Title = "C", ParentId = null, SortOrder = 2 });

        var resp = await client.PostAsJsonAsync("/api/todos/1/reorder", new ReorderDto(2, "after"));
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var roots = (await GetAll(client))
            .Where(t => t.ParentId == null).OrderBy(t => t.SortOrder).Select(t => t.Title).ToList();
        Assert.Equal(new[] { "B", "A", "C" }, roots);
    }

    [Fact]
    public async Task Reorder_Inside_MakesMovedTodoLastChildOfTarget()
    {
        var (client, db) = CreateClient();
        // A has child A1(0). B is a root. Move B inside A → B becomes A's child after A1.
        await Seed(db,
            new TodoItem { Id = 1, Title = "A",  ParentId = null, SortOrder = 0 },
            new TodoItem { Id = 2, Title = "A1", ParentId = 1,    SortOrder = 0 },
            new TodoItem { Id = 3, Title = "B",  ParentId = null, SortOrder = 1 });

        var resp = await client.PostAsJsonAsync("/api/todos/3/reorder", new ReorderDto(1, "inside"));
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var all = await GetAll(client);
        var b = all.Single(t => t.Title == "B");
        Assert.Equal(1, b.ParentId);
        var aChildren = all.Where(t => t.ParentId == 1).OrderBy(t => t.SortOrder).Select(t => t.Title).ToList();
        Assert.Equal(new[] { "A1", "B" }, aChildren);
    }

    [Fact]
    public async Task Reorder_ReturnsBadRequest_WhenDroppingTodoOntoItself()
    {
        var (client, db) = CreateClient();
        await Seed(db, new TodoItem { Id = 1, Title = "A", ParentId = null, SortOrder = 0 });
        var resp = await client.PostAsJsonAsync("/api/todos/1/reorder", new ReorderDto(1, "inside"));
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Reorder_ReturnsBadRequest_WhenDroppingTodoIntoItsOwnDescendant()
    {
        var (client, db) = CreateClient();
        // A → A1 → A2. Moving A inside A2 would create a cycle.
        await Seed(db,
            new TodoItem { Id = 1, Title = "A",  ParentId = null, SortOrder = 0 },
            new TodoItem { Id = 2, Title = "A1", ParentId = 1,    SortOrder = 0 },
            new TodoItem { Id = 3, Title = "A2", ParentId = 2,    SortOrder = 0 });

        var resp = await client.PostAsJsonAsync("/api/todos/1/reorder", new ReorderDto(3, "inside"));
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Reorder_Before_MovingIntoADifferentParent_ReparentsAndOrders()
    {
        var (client, db) = CreateClient();
        // Root A with children A1(0), A2(1). Root B. Move B before A2 → B becomes A's child between A1 and A2.
        await Seed(db,
            new TodoItem { Id = 1, Title = "A",  ParentId = null, SortOrder = 0 },
            new TodoItem { Id = 2, Title = "A1", ParentId = 1,    SortOrder = 0 },
            new TodoItem { Id = 3, Title = "A2", ParentId = 1,    SortOrder = 1 },
            new TodoItem { Id = 4, Title = "B",  ParentId = null, SortOrder = 1 });

        var resp = await client.PostAsJsonAsync("/api/todos/4/reorder", new ReorderDto(3, "before"));
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var all = await GetAll(client);
        var b = all.Single(t => t.Title == "B");
        Assert.Equal(1, b.ParentId);
        var aChildren = all.Where(t => t.ParentId == 1).OrderBy(t => t.SortOrder).Select(t => t.Title).ToList();
        Assert.Equal(new[] { "A1", "B", "A2" }, aChildren);
    }
}
