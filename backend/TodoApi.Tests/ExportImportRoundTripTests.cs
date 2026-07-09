using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.IO;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using TodoApi.Data;
using TodoApi.Models;

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
    public async Task Export_ThenImport_RestoresCommentAttachments()
    {
        var source = ClientFor("rt_att_" + Guid.NewGuid());
        var create = await source.PostAsJsonAsync("/api/todos", new { Title = "with attach", IsCompleted = false });
        var todo = await create.Content.ReadFromJsonAsync<TodoResponse>();

        // comment carrying a file attachment (server writes it to uploads + a DB row)
        var cform = new MultipartFormDataContent
        {
            { new StringContent(todo!.Id.ToString()), "todoId" },
            { new StringContent("see attached"), "text" },
        };
        var fc = new ByteArrayContent(Encoding.UTF8.GetBytes("fake png bytes"));
        fc.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        cform.Add(fc, "file_0", "photo.png");
        var cresp = await source.PostAsync("/api/comments", cform);
        Assert.True(cresp.IsSuccessStatusCode, $"comment create failed: {cresp.StatusCode}");

        var exportResp = await source.PostAsJsonAsync("/api/export/export", new { Password = "pw" });
        var backup = await exportResp.Content.ReadAsByteArrayAsync();

        var target = ClientFor("rt_att2_" + Guid.NewGuid());
        using var form = ImportForm(backup, "pw");
        var importResp = await target.PostAsync("/api/export/import", form);
        Assert.True(importResp.StatusCode == HttpStatusCode.OK,
            $"import: {importResp.StatusCode} {await importResp.Content.ReadAsStringAsync()}");

        var todos = await target.GetFromJsonAsync<List<TodoResponse>>("/api/todos");
        var restoredId = todos!.First(t => t.Title == "with attach").Id;
        var comments = await target.GetFromJsonAsync<List<CommentResponse>>($"/api/comments?todoId={restoredId}");

        Assert.NotNull(comments);
        var withAtt = comments!.FirstOrDefault(c => c.Attachments.Count > 0);
        Assert.True(withAtt is not null, "restored comment has no attachments");
        Assert.Contains(withAtt!.Attachments, a => a.Path.Contains("/uploads/"));
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
    public async Task Import_AddOnly_UpdatesRenamedAndReparentedExistingTodo()
    {
        // SOURCE: parent "P" (id 1) + child renamed "child NEW" placed under P (id 2).
        var source = ClientFor("rt_upd_src_" + Guid.NewGuid());
        await source.PostAsJsonAsync("/api/todos", new { Title = "P", IsCompleted = false });                    // id 1
        await source.PostAsJsonAsync("/api/todos", new { Title = "child NEW", IsCompleted = false, ParentId = 1 }); // id 2 under P
        var exportResp = await source.PostAsJsonAsync("/api/export/export", new { Password = "pw" });
        var backup = await exportResp.Content.ReadAsByteArrayAsync();

        // TARGET: same ids but the OLD state — child "child OLD" as a root, no parent.
        var target = ClientFor("rt_upd_dst_" + Guid.NewGuid());
        await target.PostAsJsonAsync("/api/todos", new { Title = "P", IsCompleted = false });                    // id 1
        await target.PostAsJsonAsync("/api/todos", new { Title = "child OLD", IsCompleted = false });             // id 2, root

        using var form = ImportForm(backup, "pw", "addonly");
        var importResp = await target.PostAsync("/api/export/import", form);
        Assert.True(importResp.StatusCode == HttpStatusCode.OK, await importResp.Content.ReadAsStringAsync());

        var todos = await target.GetFromJsonAsync<List<TodoResponse>>("/api/todos");
        var child = todos!.First(t => t.Id == 2);
        Assert.Equal("child NEW", child.Title);   // rename propagated
        Assert.Equal(1, child.ParentId);          // re-parenting propagated
    }

    static MultipartFormDataContent CommentForm(int todoId, string text, bool withFile)
    {
        var f = new MultipartFormDataContent
        {
            { new StringContent(todoId.ToString()), "todoId" },
            { new StringContent(text), "text" },
        };
        if (withFile)
        {
            var fc = new ByteArrayContent(Encoding.UTF8.GetBytes("filebytes"));
            fc.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            f.Add(fc, "file_0", "pic.png");
        }
        return f;
    }

    [Fact]
    public async Task Import_AddOnly_MergesExistingComment_TextAndAddedAttachment()
    {
        // SOURCE: todo 1, comment 1 = "NEW text" + one attachment.
        var source = ClientFor("rt_cm1_src_" + Guid.NewGuid());
        await source.PostAsJsonAsync("/api/todos", new { Title = "T", IsCompleted = false }); // id 1
        using (var cf = CommentForm(1, "NEW text", withFile: true))
            Assert.True((await source.PostAsync("/api/comments", cf)).IsSuccessStatusCode);
        var backup = await (await source.PostAsJsonAsync("/api/export/export", new { Password = "pw" }))
            .Content.ReadAsByteArrayAsync();

        // TARGET: same ids but stale — comment 1 = "OLD text", no attachment.
        var target = ClientFor("rt_cm1_dst_" + Guid.NewGuid());
        await target.PostAsJsonAsync("/api/todos", new { Title = "T", IsCompleted = false }); // id 1
        using (var cf = CommentForm(1, "OLD text", withFile: false))
            await target.PostAsync("/api/comments", cf);                                       // comment 1, no attachment

        using var form = ImportForm(backup, "pw", "addonly");
        Assert.Equal(HttpStatusCode.OK, (await target.PostAsync("/api/export/import", form)).StatusCode);

        var comments = await target.GetFromJsonAsync<List<CommentResponse>>("/api/comments?todoId=1");
        var c = comments!.First(x => x.Id == 1);
        Assert.Equal("NEW text", c.Text);   // text merged
        Assert.Single(c.Attachments);        // attachment added to the existing comment
    }

    [Fact]
    public async Task Import_AddOnly_RemovesAttachmentDroppedFromBackup()
    {
        // SOURCE: comment 1 with NO attachment.
        var source = ClientFor("rt_cm2_src_" + Guid.NewGuid());
        await source.PostAsJsonAsync("/api/todos", new { Title = "T", IsCompleted = false });
        using (var cf = CommentForm(1, "same", withFile: false))
            await source.PostAsync("/api/comments", cf);
        var backup = await (await source.PostAsJsonAsync("/api/export/export", new { Password = "pw" }))
            .Content.ReadAsByteArrayAsync();

        // TARGET: comment 1 WITH an attachment the backup no longer has.
        var target = ClientFor("rt_cm2_dst_" + Guid.NewGuid());
        await target.PostAsJsonAsync("/api/todos", new { Title = "T", IsCompleted = false });
        using (var cf = CommentForm(1, "same", withFile: true))
            await target.PostAsync("/api/comments", cf);

        // capture the on-disk file so we can assert it gets pruned, not just its DB row
        var before = await target.GetFromJsonAsync<List<CommentResponse>>("/api/comments?todoId=1");
        var fileName = Path.GetFileName(before!.First(x => x.Id == 1).Attachments.Single().Path);
        var filePath = Path.Combine(TodoApi.DataPaths.Uploads, fileName);
        Assert.True(File.Exists(filePath), "attachment file should exist before import");

        using var form = ImportForm(backup, "pw", "addonly");
        Assert.Equal(HttpStatusCode.OK, (await target.PostAsync("/api/export/import", form)).StatusCode);

        var comments = await target.GetFromJsonAsync<List<CommentResponse>>("/api/comments?todoId=1");
        var c = comments!.First(x => x.Id == 1);
        Assert.Empty(c.Attachments);          // attachment absent from backup → DB row removed
        Assert.False(File.Exists(filePath));  // …and the orphaned file is deleted from disk
    }

    [Fact]
    public async Task DeleteTodo_DeletesSubtreeComments_AndTheirAttachmentFiles()
    {
        var client = ClientFor("del_" + Guid.NewGuid());
        var p = await (await client.PostAsJsonAsync("/api/todos", new { Title = "P", IsCompleted = false }))
            .Content.ReadFromJsonAsync<TodoResponse>();
        var s = await (await client.PostAsJsonAsync("/api/todos", new { Title = "S", IsCompleted = false, ParentId = p!.Id }))
            .Content.ReadFromJsonAsync<TodoResponse>();
        using (var cf = CommentForm(s!.Id, "note", withFile: true))
            Assert.True((await client.PostAsync("/api/comments", cf)).IsSuccessStatusCode);

        var comments = await client.GetFromJsonAsync<List<CommentResponse>>($"/api/comments?todoId={s.Id}");
        var filePath = Path.Combine(TodoApi.DataPaths.Uploads, Path.GetFileName(comments!.Single().Attachments.Single().Path));
        Assert.True(File.Exists(filePath), "attachment file should exist before delete");

        // Delete the PARENT — the subtask, its comment, and the file must all go with it.
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/todos/{p.Id}")).StatusCode);

        var todos = await client.GetFromJsonAsync<List<TodoResponse>>("/api/todos");
        Assert.DoesNotContain(todos!, t => t.Id == p.Id || t.Id == s.Id);   // whole subtree gone
        var after = await client.GetFromJsonAsync<List<CommentResponse>>($"/api/comments?todoId={s.Id}");
        Assert.Empty(after!);                                                // orphaned comment gone
        Assert.False(File.Exists(filePath));                                 // attachment file deleted
    }

    private static async Task Seed(string dbName, Action<AppDbContext> seed)
    {
        var options = new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName).Options;
        using var db = new AppDbContext(options);
        seed(db);
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Import_PrunesOrphanedComments_AndTheirFiles()
    {
        // A comment left behind by a since-deleted todo (TodoId points at nothing) + its file.
        var dbName = "orphanc_" + Guid.NewGuid();
        var target = ClientFor(dbName);
        var fileName = Guid.NewGuid().ToString("N") + ".png";
        var filePath = Path.Combine(TodoApi.DataPaths.Uploads, fileName);
        Directory.CreateDirectory(TodoApi.DataPaths.Uploads);
        File.WriteAllText(filePath, "orphan");
        await Seed(dbName, db =>
        {
            db.Comments.Add(new Comment { Id = 1, TodoId = 99999, Text = "orphan" });
            db.CommentAttachments.Add(new CommentAttachment { Id = 1, CommentId = 1, Path = "/uploads/" + fileName, Type = "image" });
        });
        Assert.True(File.Exists(filePath), "orphan file should exist before import");

        // Any valid backup imported addonly triggers the cleanup.
        var src = ClientFor("orphanc_src_" + Guid.NewGuid());
        await src.PostAsJsonAsync("/api/todos", new { Title = "x", IsCompleted = false });
        var backup = await (await src.PostAsJsonAsync("/api/export/export", new { Password = "pw" }))
            .Content.ReadAsByteArrayAsync();

        using var form = ImportForm(backup, "pw", "addonly");
        Assert.Equal(HttpStatusCode.OK, (await target.PostAsync("/api/export/import", form)).StatusCode);

        Assert.False(File.Exists(filePath), "orphaned comment's file should be pruned");
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

    private record TodoResponse(int Id, string Title, bool IsCompleted, string Status, int? ParentId);
    private record CommentResponse(int Id, int TodoId, string Text, List<AttachmentResponse> Attachments);
    private record AttachmentResponse(int Id, string Path, string? FileName);
}
