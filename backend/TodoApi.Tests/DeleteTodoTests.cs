using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Json;
using TodoApi.Data;

namespace TodoApi.Tests;

public class DeleteTodoTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public DeleteTodoTests(WebApplicationFactory<Program> factory)
    {
        var dbName = "TestDb_Delete_" + Guid.NewGuid();
        _client = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                var descriptor = services.SingleOrDefault(
                    d => d.ServiceType == typeof(DbContextOptions<AppDbContext>));
                if (descriptor != null)
                    services.Remove(descriptor);

                services.AddDbContext<AppDbContext>(options =>
                    options.UseInMemoryDatabase(dbName));
            });
        }).CreateClient();
    }

    [Fact]
    public async Task Delete_ReturnsNotFound_WhenTodoDoesNotExist()
    {
        var response = await _client.DeleteAsync("/api/todos/99999");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Delete_ReturnsNoContent_WhenTodoExists()
    {
        var created = await _client.PostAsJsonAsync("/api/todos", new { Title = "To delete", IsCompleted = false });
        var body = await created.Content.ReadFromJsonAsync<TodoResponse>();
        var id = body!.Id;

        var response = await _client.DeleteAsync($"/api/todos/{id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task Delete_Succeeds_WhenSubtaskParentNoLongerExists()
    {
        // A subtask can be left pointing at a parent id that's already gone
        // (legacy/orphan data). Deleting it must not try to log against the
        // missing parent — that would hit a FK constraint on a real DB.
        var created = await _client.PostAsJsonAsync("/api/todos",
            new { Title = "Orphan child", IsCompleted = false, ParentId = 424242 });
        var body = await created.Content.ReadFromJsonAsync<TodoResponse>();
        var id = body!.Id;

        var response = await _client.DeleteAsync($"/api/todos/{id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    private record TodoResponse(int Id, string Title, bool IsCompleted, string Status);
}
