using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Json;
using TodoApi.Data;

namespace TodoApi.Tests;

public class CreateTodoTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public CreateTodoTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                var descriptor = services.SingleOrDefault(
                    d => d.ServiceType == typeof(DbContextOptions<AppDbContext>));
                if (descriptor != null)
                    services.Remove(descriptor);

                services.AddDbContext<AppDbContext>(options =>
                    options.UseInMemoryDatabase("TestDb_Create_" + Guid.NewGuid()));
            });
        }).CreateClient();
    }

    [Fact]
    public async Task CreateTodo_ReturnsBadRequest_WhenTitleIsEmpty()
    {
        var response = await _client.PostAsJsonAsync("/api/todos", new { Title = "", IsCompleted = false });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateTodo_ReturnsBadRequest_WhenTitleIsWhitespace()
    {
        var response = await _client.PostAsJsonAsync("/api/todos", new { Title = "   ", IsCompleted = false });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateTodo_ReturnsCreated_WithTodoBody_WhenTitleIsValid()
    {
        var response = await _client.PostAsJsonAsync("/api/todos", new { Title = "Buy groceries", IsCompleted = false });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<TodoResponse>();
        Assert.NotNull(body);
        Assert.Equal("Buy groceries", body.Title);
        Assert.True(body.Id > 0);
    }

    private record TodoResponse(int Id, string Title, bool IsCompleted, string Status);
}
