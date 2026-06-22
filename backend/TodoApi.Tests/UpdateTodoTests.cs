using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Json;
using TodoApi.Data;

namespace TodoApi.Tests;

public class UpdateTodoTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public UpdateTodoTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    private HttpClient CreateClient()
    {
        var dbName = "TestDb_Update_" + Guid.NewGuid();
        return _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                var descriptor = services.SingleOrDefault(
                    d => d.ServiceType == typeof(DbContextOptions<AppDbContext>));
                if (descriptor != null) services.Remove(descriptor);

                services.AddDbContext<AppDbContext>(options =>
                    options.UseInMemoryDatabase(dbName));
            });
        }).CreateClient();
    }

    [Fact]
    public async Task UpdateTodo_ReturnsOk_WithUpdatedFields_WhenTodoExists()
    {
        var client = CreateClient();
        var created = await client.PostAsJsonAsync("/api/todos", new { Title = "Original", IsCompleted = false });
        var body = await created.Content.ReadFromJsonAsync<TodoResponse>();
        var id = body!.Id;

        var response = await client.PutAsJsonAsync($"/api/todos/{id}",
            new { Title = "Updated", IsCompleted = true, Status = "done", DueDate = (string?)null });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.Content.ReadFromJsonAsync<TodoResponse>();
        Assert.Equal("Updated", updated!.Title);
        Assert.True(updated.IsCompleted);
    }

    private record TodoResponse(int Id, string Title, bool IsCompleted, string Status);

    [Fact]
    public async Task UpdateTodo_ReturnsNotFound_WhenTodoDoesNotExist()
    {
        var client = CreateClient();

        var response = await client.PutAsJsonAsync("/api/todos/99999",
            new { Title = "Ghost", IsCompleted = false, Status = "nothing", DueDate = (string?)null });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
