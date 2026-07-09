using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Net.Http.Json;
using System.Text.Json;
using TodoApi.Data;
using TodoApi.Models;

namespace TodoApi.Tests;

public class SearchControllerTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public SearchControllerTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    private (HttpClient client, string dbName) CreateClient()
    {
        var dbName = "TestDb_Search_" + Guid.NewGuid();
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

    private async Task Seed(string dbName, Action<AppDbContext> seed)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName).Options;
        using var db = new AppDbContext(options);
        seed(db);
        await db.SaveChangesAsync();
    }

    // Returns the set of todo ids present in the search response.
    private static async Task<HashSet<int>> SearchTodoIds(HttpClient client, string q)
    {
        var json = await client.GetStringAsync($"/api/search?q={Uri.EscapeDataString(q)}");
        using var doc = JsonDocument.Parse(json);
        var ids = new HashSet<int>();
        foreach (var r in doc.RootElement.EnumerateArray())
            ids.Add(r.GetProperty("todoId").GetInt32());
        return ids;
    }

    // The pageNumber from the first attachment match in the response (null if absent).
    private static async Task<int?> FirstAttachmentPageNumber(HttpClient client, string q)
    {
        var json = await client.GetStringAsync($"/api/search?q={Uri.EscapeDataString(q)}");
        using var doc = JsonDocument.Parse(json);
        foreach (var r in doc.RootElement.EnumerateArray())
            foreach (var m in r.GetProperty("matches").EnumerateArray())
                if (m.GetProperty("source").GetString() == "attachment"
                    && m.TryGetProperty("pageNumber", out var pn) && pn.ValueKind == JsonValueKind.Number)
                    return pn.GetInt32();
        return null;
    }

    [Fact]
    public async Task Search_ReturnsPageNumber_OfTheFirstPageContainingTheQuery()
    {
        var (client, db) = CreateClient();
        var pages = System.Text.Json.JsonSerializer.Serialize(new[]
        {
            "strana jedna bez",
            "strana dva bez",
            "tady je HledanyVyraz na treti strane",
        });
        await Seed(db, c =>
        {
            c.Todos.Add(new TodoItem { Id = 1, Title = "kuchařka" });
            c.Comments.Add(new Comment { Id = 1, TodoId = 1, Text = "" });
            c.CommentAttachments.Add(new CommentAttachment
            {
                Id = 1, CommentId = 1, Path = "/uploads/x.pdf", Type = "file",
                ExtractedText = "strana jedna bez strana dva bez tady je HledanyVyraz na treti strane",
                PageTexts = pages,
            });
        });

        var page = await FirstAttachmentPageNumber(client, "HledanyVyraz");

        Assert.Equal(3, page);
    }

    [Fact]
    public async Task Search_ReturnsFirstPage_WhenPhraseSpansTwoConsecutivePages()
    {
        // A phrase can straddle a page break ("…grilované" at the end of p2,
        // "kuře…" at the start of p3). The flat text finds it, so the result
        // appears; the page jump should land on the FIRST of the two pages.
        var (client, db) = CreateClient();
        var pages = System.Text.Json.JsonSerializer.Serialize(new[]
        {
            "uvod nic",
            "predkrmy a grilovane",
            "kure s omackou dezert",
        });
        await Seed(db, c =>
        {
            c.Todos.Add(new TodoItem { Id = 1, Title = "kuchařka" });
            c.Comments.Add(new Comment { Id = 1, TodoId = 1, Text = "" });
            c.CommentAttachments.Add(new CommentAttachment
            {
                Id = 1, CommentId = 1, Path = "/uploads/x.pdf", Type = "file",
                ExtractedText = "uvod nic predkrmy a grilovane kure s omackou dezert",
                PageTexts = pages,
            });
        });

        var page = await FirstAttachmentPageNumber(client, "grilovane kure");

        Assert.Equal(2, page);
    }

    [Fact]
    public async Task Search_PageNumberIsNull_WhenAttachmentHasNoPageTexts()
    {
        var (client, db) = CreateClient();
        await Seed(db, c =>
        {
            c.Todos.Add(new TodoItem { Id = 1, Title = "kuchařka" });
            c.Comments.Add(new Comment { Id = 1, TodoId = 1, Text = "" });
            c.CommentAttachments.Add(new CommentAttachment
            {
                Id = 1, CommentId = 1, Path = "/uploads/x.pdf", Type = "file",
                ExtractedText = "tady je HledanyVyraz nekde",
                PageTexts = null,
            });
        });

        var page = await FirstAttachmentPageNumber(client, "HledanyVyraz");

        Assert.Null(page);
    }

    [Fact]
    public async Task Search_FindsMultiWordPhrase_WhenAttachmentTextHasNoSpaceBetweenWords()
    {
        // A PDF whose phrase spans two lines often extracts with the words fused
        // ("celé\nkuře" → "celékuře"). Searching the natural phrase "celé kuře"
        // (with a space) must still match.
        var (client, db) = CreateClient();
        await Seed(db, c =>
        {
            c.Todos.Add(new TodoItem { Id = 1, Title = "kuchařka" });
            c.Comments.Add(new Comment { Id = 1, TodoId = 1, Text = "" });
            c.CommentAttachments.Add(new CommentAttachment
            {
                Id = 1, CommentId = 1, Path = "/uploads/x.pdf", Type = "file",
                ExtractedText = "Grilovane celékuře na panvi recept",
            });
        });

        var ids = await SearchTodoIds(client, "celé kuře");

        Assert.Contains(1, ids);
    }

    [Fact]
    public async Task Search_FindsMultiWordPhrase_WhenAttachmentTextHasExtraSpaces()
    {
        var (client, db) = CreateClient();
        await Seed(db, c =>
        {
            c.Todos.Add(new TodoItem { Id = 1, Title = "kuchařka" });
            c.Comments.Add(new Comment { Id = 1, TodoId = 1, Text = "" });
            c.CommentAttachments.Add(new CommentAttachment
            {
                Id = 1, CommentId = 1, Path = "/uploads/x.pdf", Type = "file",
                ExtractedText = "recept na celé    kuře dnes",
            });
        });

        var ids = await SearchTodoIds(client, "celé kuře");

        Assert.Contains(1, ids);
    }

    [Fact]
    public async Task Search_DoesNotMatch_WhenWordsAreGenuinelyAbsent()
    {
        var (client, db) = CreateClient();
        await Seed(db, c =>
        {
            c.Todos.Add(new TodoItem { Id = 1, Title = "kuchařka" });
            c.Comments.Add(new Comment { Id = 1, TodoId = 1, Text = "" });
            c.CommentAttachments.Add(new CommentAttachment
            {
                Id = 1, CommentId = 1, Path = "/uploads/x.pdf", Type = "file",
                ExtractedText = "recept na rybí polévku",
            });
        });

        var ids = await SearchTodoIds(client, "celé kuře");

        Assert.DoesNotContain(1, ids);
    }

    [Fact]
    public async Task Search_StillMatchesPlainTitle()
    {
        // Guard: the whitespace-insensitive change must not break ordinary matching.
        var (client, db) = CreateClient();
        await Seed(db, c => c.Todos.Add(new TodoItem { Id = 1, Title = "nakoupit mléko" }));

        var ids = await SearchTodoIds(client, "mléko");

        Assert.Contains(1, ids);
    }

    [Fact]
    public async Task Search_FindsTitle_ByMultipleWordsInAnyOrder()
    {
        // "dovolena seznam" (reversed order, no diacritics) must find a title that contains
        // both words somewhere, in any order.
        var (client, db) = CreateClient();
        await Seed(db, c => c.Todos.Add(new TodoItem { Id = 1, Title = "seznam na dovolenou, dovolena vzít sebou" }));

        var ids = await SearchTodoIds(client, "dovolena seznam");

        Assert.Contains(1, ids);
    }

    [Fact]
    public async Task Search_TitleMultiWord_IsAccentInsensitive()
    {
        var (client, db) = CreateClient();
        await Seed(db, c => c.Todos.Add(new TodoItem { Id = 1, Title = "seznam na dovolenou, dovolena vzít sebou" }));

        var ids = await SearchTodoIds(client, "dovolená seznam");

        Assert.Contains(1, ids);
    }

    [Fact]
    public async Task Search_TitleMultiWord_RequiresAllWords_NotJustOne()
    {
        // AND semantics: one word present, the other absent → no match.
        var (client, db) = CreateClient();
        await Seed(db, c => c.Todos.Add(new TodoItem { Id = 1, Title = "seznam na dovolenou" }));

        var ids = await SearchTodoIds(client, "seznam chybejici");

        Assert.DoesNotContain(1, ids);
    }
}
