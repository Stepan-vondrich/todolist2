using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Json;
using TodoApi.Data;
using TodoApi.Models;

namespace TodoApi.Tests;

public class GetTodosTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public GetTodosTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    private HttpClient CreateClient(string dbName) =>
        _factory.WithWebHostBuilder(builder =>
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

    [Fact]
    public async Task GetAll_ReturnsOk_WithEmptyList_WhenNoTodosExist()
    {
        var client = CreateClient("TestDb_Get_Empty_" + Guid.NewGuid());

        var response = await client.GetAsync("/api/todos");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var todos = await response.Content.ReadFromJsonAsync<List<object>>();
        Assert.NotNull(todos);
        Assert.Empty(todos);
    }

    [Fact]
    public async Task GetAll_ReturnsAllCreatedTodos()
    {
        var client = CreateClient("TestDb_Get_All_" + Guid.NewGuid());

        await client.PostAsJsonAsync("/api/todos", new { Title = "First", IsCompleted = false });
        await client.PostAsJsonAsync("/api/todos", new { Title = "Second", IsCompleted = false });

        var response = await client.GetAsync("/api/todos");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var todos = await response.Content.ReadFromJsonAsync<List<object>>();
        Assert.NotNull(todos);
        Assert.Equal(2, todos.Count);
    }
}
