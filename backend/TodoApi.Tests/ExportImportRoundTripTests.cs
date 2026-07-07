using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using TodoApi.Data;

namespace TodoApi.Tests;

// End-to-end proof that the streaming import path (decrypt-to-temp-file → unzip-from-disk →
// deserialize → DB restore) actually reconstructs an exported backup, and rejects a wrong
// password. The backup is created by one app instance and imported into a *fresh* one — the
// realistic "restore onto a clean server" scenario.
public class ExportImportRoundTripTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public ExportImportRoundTripTests(WebApplicationFactory<Program> factory) => _factory = factory;

    // A client bound to its own in-memory DB. The name is captured once (not regenerated in
    // the options lambda, which runs per request and would give each request an empty DB).
    private HttpClient ClientFor(string dbName) =>
        _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                var descriptor = services.SingleOrDefault(
                    d => d.ServiceType == typeof(DbContextOptions<AppDbContext>));
                if (descriptor != null) services.Remove(descriptor);
                services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(dbName));
            });
        }).CreateClient();

    static MultipartFormDataContent ImportForm(byte[] backup, string password, string mode = "replace")
    {
        var form = new MultipartFormDataContent
        {
            { new StringContent(password), "password" },
            { new StringContent(mode), "mode" },
        };
        var file = new ByteArrayContent(backup);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(file, "file", "todolist.backup");
        return form;
    }

    static async Task<byte[]> MakeBackupWithTodo(HttpClient source, string title, string password)
    {
        var create = await source.PostAsJsonAsync("/api/todos", new { Title = title, IsCompleted = false });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);

        var exportResp = await source.PostAsJsonAsync("/api/export/export", new { Password = password });
        Assert.Equal(HttpStatusCode.OK, exportResp.StatusCode);
        return await exportResp.Content.ReadAsByteArrayAsync();
    }

    [Fact]
    public async Task Export_ThenStreamingImport_RestoresTodos()
    {
        var source = ClientFor("rt_src_" + Guid.NewGuid());
        var backup = await MakeBackupWithTodo(source, "Backup me", "pw123");
        Assert.True(backup.Length > 0);

        var target = ClientFor("rt_dst_" + Guid.NewGuid());
        using var form = ImportForm(backup, "pw123");
        var importResp = await target.PostAsync("/api/export/import", form);
        var importBody = await importResp.Content.ReadAsStringAsync();
        Assert.True(importResp.StatusCode == HttpStatusCode.OK, $"import status={importResp.StatusCode} body={importBody}");

        var todos = await target.GetFromJsonAsync<List<TodoResponse>>("/api/todos");
        Assert.NotNull(todos);
        Assert.Contains(todos!, t => t.Title == "Backup me");
    }

    [Fact]
    public async Task Import_FileBeforePassword_ReturnsBadRequest()
    {
        var source = ClientFor("rt_ord_" + Guid.NewGuid());
        var backup = await MakeBackupWithTodo(source, "ordered", "pw");

        var target = ClientFor("rt_ord2_" + Guid.NewGuid());
        // file FIRST, password AFTER: the streaming reader has no key when the file arrives.
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(backup);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(file, "file", "todolist.backup");
        form.Add(new StringContent("pw"), "password");
        form.Add(new StringContent("replace"), "mode");

        var resp = await target.PostAsync("/api/export/import", form);
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Import_WrongPassword_ReturnsBadRequest()
    {
        var source = ClientFor("rt_wp_" + Guid.NewGuid());
        var backup = await MakeBackupWithTodo(source, "secret task", "right");

        var target = ClientFor("rt_wp2_" + Guid.NewGuid());
        using var form = ImportForm(backup, "wrong");
        var importResp = await target.PostAsync("/api/export/import", form);
        Assert.Equal(HttpStatusCode.BadRequest, importResp.StatusCode);
    }

    private record TodoResponse(int Id, string Title, bool IsCompleted, string Status);
}
