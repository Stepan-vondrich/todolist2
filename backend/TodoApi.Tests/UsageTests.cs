using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Json;
using TodoApi.Data;

namespace TodoApi.Tests;

public class UsageTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public UsageTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                var descriptor = services.SingleOrDefault(
                    d => d.ServiceType == typeof(DbContextOptions<AppDbContext>));
                if (descriptor != null) services.Remove(descriptor);
                services.AddDbContext<AppDbContext>(options =>
                    options.UseInMemoryDatabase("TestDb_Usage_" + Guid.NewGuid()));
            });
        }).CreateClient();
    }

    [Fact]
    public async Task Usage_Returns200_WithDbAndUploads()
    {
        var res = await _client.GetAsync("/api/usage");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var body = await res.Content.ReadFromJsonAsync<UsageResponse>();
        Assert.NotNull(body);
        Assert.NotNull(body!.Db);
        Assert.NotNull(body.Uploads);
        Assert.True(body.Db!.LimitBytes > 0);      // Neon 0.5 GB cap surfaced
        Assert.True(body.Db.UsedBytes >= 0);
        Assert.True(body.Uploads!.UsedBytes >= 0);
    }

    private record UsageResponse(DbInfo Db, UploadInfo Uploads);
    private record DbInfo(string Provider, long UsedBytes, long LimitBytes);
    private record UploadInfo(long UsedBytes, int FileCount);
}
