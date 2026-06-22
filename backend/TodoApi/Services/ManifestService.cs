using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TodoApi.Data;
using TodoApi.Models;
using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace TodoApi.Services;

/// <summary>Thrown when an incoming manifest fails validation; surfaced as HTTP 400.</summary>
public class ManifestValidationException(string message) : Exception(message);

/// <summary>Parsed contents of <see cref="PlannerSettings.Json"/>.</summary>
public class PlannerSettingsData
{
    public NastaveniDto Nastaveni { get; set; } = ManifestService.DefaultNastaveni();
    public string ManifestFileHash { get; set; } = string.Empty;
    public string ManifestFileMtimeUtc { get; set; } = string.Empty;
}

/// <summary>Result of comparing the on-disk manifest file with the last version we wrote.</summary>
public record ManifestStatus(bool FileExists, bool ExternalChange, string? FileMtimeUtc, int FileTaskCount);

/// <summary>
/// Bridges the DB (source of truth) and the YAML manifest file (a bidirectional mirror).
/// DB → file via <see cref="WriteFileFromDbAsync"/>; file → DB via <see cref="ApplyToDbAsync"/>.
/// External hand-edits are detected by SHA-256 hash comparison rather than auto-applied.
/// </summary>
public class ManifestService(AppDbContext db)
{
    static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    readonly IDeserializer _yamlIn = new DeserializerBuilder()
        .IgnoreUnmatchedProperties()
        .Build();

    readonly ISerializer _yamlOut = new SerializerBuilder()
        .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull)
        .WithNamingConvention(NullNamingConvention.Instance) // aliases come from [YamlMember]
        .Build();

    // ── file location ─────────────────────────────────────────────────────────
    /// <summary>manifest.yaml lives at the project root (the directory containing .git),
    /// so the user can edit it easily. Falls back to the content root for a published exe.</summary>
    public static string ResolveManifestPath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, ".git")))
                return Path.Combine(dir.FullName, "manifest.yaml");
            dir = dir.Parent;
        }
        return Path.Combine(AppContext.BaseDirectory, "manifest.yaml");
    }

    // ── YAML (de)serialization ──────────────────────────────────────────────────
    public string Serialize(ManifestDto dto)
    {
        // Emit with 3-space indentation to match the agreed hand-edit style.
        using var sw = new StringWriter();
        var emitter = new Emitter(sw, EmitterSettings.Default.WithBestIndent(3).WithIndentedSequences());
        _yamlOut.Serialize(emitter, dto);
        return sw.ToString();
    }

    public ManifestDto Deserialize(string yaml)
    {
        try
        {
            return _yamlIn.Deserialize<ManifestDto>(yaml) ?? new ManifestDto();
        }
        catch (YamlException ex)
        {
            var where = ex.Start.Line > 0 ? $" (řádek {ex.Start.Line})" : "";
            throw new ManifestValidationException($"Chyba v YAML{where}: {ex.Message}");
        }
    }

    // ── DB → DTO ─────────────────────────────────────────────────────────────────
    public async Task<ManifestDto> BuildFromDbAsync()
    {
        var manifests = await db.TaskManifests.ToListAsync();
        var todoIds = manifests.Select(m => m.TodoId).ToList();
        var todos = await db.Todos.Where(t => todoIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id);

        var tasky = new List<ManifestTaskDto>();
        foreach (var m in manifests.OrderBy(m => m.Id))
        {
            todos.TryGetValue(m.TodoId, out var todo);
            tasky.Add(new ManifestTaskDto
            {
                Id = m.Slug,
                Title = todo?.Title ?? string.Empty,
                Odhad = m.Odhad,
                Dependencies = DeJsonList(m.Dependencies),
                MuzuZacit = FmtDate(m.MuzuZacit),
                Status = string.IsNullOrEmpty(todo?.Status) ? null : todo!.Status,
                Deadline = FmtDate(m.Deadline),
                Kdy = NullIfEmpty(DeJsonList(m.Kdy)),
                JenVPraci = m.JenVPraci ? true : null,
                MuzeBezetS = NullIfEmpty(DeJsonList(m.MuzeBezetS)),
                CekaNaCloveka = DeCekaNaCloveka(m.CekaNaCloveka),
                PevnyCas = string.IsNullOrWhiteSpace(m.PevnyCas) ? null : m.PevnyCas,
                Periodicita = string.IsNullOrWhiteSpace(m.Periodicita) ? null : m.Periodicita,
            });
        }

        var settings = await LoadSettingsAsync();
        return new ManifestDto { Nastaveni = settings.Nastaveni, Tasky = tasky };
    }

    // ── DTO → DB (additive upsert by slug; never deletes tasks absent from manifest) ──
    public async Task ApplyToDbAsync(ManifestDto dto)
    {
        Validate(dto);

        // Settings
        if (dto.Nastaveni is not null)
        {
            var data = await LoadSettingsAsync();
            data.Nastaveni = dto.Nastaveni;
            await SaveSettingsAsync(data);
        }

        var existing = await db.TaskManifests.ToListAsync();
        var bySlug = existing.ToDictionary(m => m.Slug);

        var maxRootOrder = await db.Todos.Where(t => t.ParentId == null)
            .Select(t => (int?)t.SortOrder).MaxAsync() ?? -1;

        foreach (var t in dto.Tasky)
        {
            if (bySlug.TryGetValue(t.Id, out var manifest))
            {
                // update existing
                var todo = await db.Todos.FindAsync(manifest.TodoId);
                if (todo is not null)
                {
                    todo.Title = t.Title;
                    todo.Status = t.Status ?? string.Empty;
                    todo.IsCompleted = todo.Status == "done";
                }
                ApplyFields(manifest, t);
                Log(manifest.TodoId, "manifest_edit", new { slug = t.Id, action = "update" });
            }
            else
            {
                // create new TodoItem + TaskManifest
                var todo = new TodoItem
                {
                    Title = t.Title,
                    Status = t.Status ?? string.Empty,
                    IsCompleted = (t.Status ?? string.Empty) == "done",
                    CreatedAt = DateTime.UtcNow,
                    SortOrder = ++maxRootOrder,
                };
                db.Todos.Add(todo);
                await db.SaveChangesAsync(); // assign Id

                manifest = new TaskManifest { TodoId = todo.Id, Slug = t.Id };
                ApplyFields(manifest, t);
                db.TaskManifests.Add(manifest);
                Log(todo.Id, "manifest_edit", new { slug = t.Id, action = "create" });
            }
        }

        await db.SaveChangesAsync();
    }

    void ApplyFields(TaskManifest m, ManifestTaskDto t)
    {
        m.Odhad = t.Odhad;
        m.MuzuZacit = ParseDate(t.MuzuZacit);
        m.Deadline = ParseDate(t.Deadline);
        m.JenVPraci = t.JenVPraci ?? false;
        m.Dependencies = EnJsonList(t.Dependencies);
        m.Kdy = EnJsonList(t.Kdy);
        m.MuzeBezetS = EnJsonList(t.MuzeBezetS);
        m.CekaNaCloveka = t.CekaNaCloveka is null ? string.Empty
            : JsonSerializer.Serialize(t.CekaNaCloveka, Json);
        m.PevnyCas = t.PevnyCas ?? string.Empty;
        m.Periodicita = t.Periodicita ?? string.Empty;
    }

    static void Validate(ManifestDto dto)
    {
        var seen = new HashSet<string>();
        foreach (var t in dto.Tasky)
        {
            if (string.IsNullOrWhiteSpace(t.Id))
                throw new ManifestValidationException("Každý task musí mít 'id'.");
            if (!seen.Add(t.Id))
                throw new ManifestValidationException($"Duplicitní id '{t.Id}'.");
            if (string.IsNullOrWhiteSpace(t.Title))
                throw new ManifestValidationException($"Task '{t.Id}' nemá 'title'.");
            if (string.IsNullOrWhiteSpace(t.Odhad))
                throw new ManifestValidationException($"Task '{t.Id}' nemá povinný 'odhad'.");
            // Note: an empty `dependencies:` line parses to null in YAML and is treated as "no deps".
            if (string.IsNullOrWhiteSpace(t.MuzuZacit))
                throw new ManifestValidationException($"Task '{t.Id}' nemá povinné 'muzu_zacit'.");
        }
        // dependencies / muze_bezet_s must reference known slugs
        foreach (var t in dto.Tasky)
        {
            foreach (var dep in t.Dependencies ?? new())
                if (!seen.Contains(dep))
                    throw new ManifestValidationException($"Task '{t.Id}' závisí na neznámém id '{dep}'.");
        }
    }

    // ── file sync ─────────────────────────────────────────────────────────────
    public async Task WriteFileFromDbAsync()
    {
        var yaml = Serialize(await BuildFromDbAsync());
        await WriteTextAndRecordAsync(yaml);
    }

    public async Task WriteTextAndRecordAsync(string yaml)
    {
        var path = ResolveManifestPath();
        await File.WriteAllTextAsync(path, yaml, new UTF8Encoding(false));
        var data = await LoadSettingsAsync();
        data.ManifestFileHash = Hash(yaml);
        data.ManifestFileMtimeUtc = File.GetLastWriteTimeUtc(path).ToString("o");
        await SaveSettingsAsync(data);
    }

    public async Task<ManifestStatus> GetStatusAsync()
    {
        var path = ResolveManifestPath();
        if (!File.Exists(path))
            return new ManifestStatus(false, false, null, 0);

        var text = await File.ReadAllTextAsync(path);
        var data = await LoadSettingsAsync();
        var external = !string.IsNullOrEmpty(data.ManifestFileHash) && Hash(text) != data.ManifestFileHash;
        int count = 0;
        try { count = Deserialize(text).Tasky.Count; } catch { /* malformed file: report 0 */ }
        return new ManifestStatus(true, external, File.GetLastWriteTimeUtc(path).ToString("o"), count);
    }

    /// <summary>Adopt the on-disk file into the DB, then re-record its hash so it is no longer "external".</summary>
    public async Task ReloadFromFileAsync()
    {
        var path = ResolveManifestPath();
        if (!File.Exists(path))
            throw new ManifestValidationException("Soubor manifest.yaml na disku neexistuje.");
        var text = await File.ReadAllTextAsync(path);
        await ApplyToDbAsync(Deserialize(text));
        await WriteFileFromDbAsync(); // canonicalize + record hash
    }

    // ── settings store ──────────────────────────────────────────────────────────
    public async Task<PlannerSettingsData> LoadSettingsAsync()
    {
        var row = await db.PlannerSettings.FindAsync(PlannerSettings.SingletonId);
        if (row is null || string.IsNullOrWhiteSpace(row.Json))
            return new PlannerSettingsData();
        try { return JsonSerializer.Deserialize<PlannerSettingsData>(row.Json, Json) ?? new(); }
        catch { return new PlannerSettingsData(); }
    }

    public async Task SaveSettingsAsync(PlannerSettingsData data)
    {
        var row = await db.PlannerSettings.FindAsync(PlannerSettings.SingletonId);
        var json = JsonSerializer.Serialize(data, Json);
        if (row is null)
            db.PlannerSettings.Add(new PlannerSettings { Id = PlannerSettings.SingletonId, Json = json });
        else
            row.Json = json;
        await db.SaveChangesAsync();
    }

    public static NastaveniDto DefaultNastaveni() => new()
    {
        HorizontPlanovani = "3m",
        PracovniDoba = new() { ["po-pa"] = "09:00-17:00" },
        OknaDne = new()
        {
            ["rano"] = "06:00-11:00",
            ["dopo"] = "11:00-14:00",
            ["odpo"] = "14:00-18:00",
            ["vecer"] = "18:00-23:00",
        },
        ReakceLidi = new() { ["rychle"] = "1d", ["normalne"] = "3d", ["pomalu"] = "7d" },
    };

    // ── helpers ──────────────────────────────────────────────────────────────────
    void Log(int todoId, string eventType, object? detail = null) =>
        db.TaskLogs.Add(new TaskLog
        {
            TodoId = todoId,
            Timestamp = DateTime.UtcNow,
            EventType = eventType,
            Detail = detail is null ? null : JsonSerializer.Serialize(detail, Json),
        });

    static string Hash(string s)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(s.Replace("\r\n", "\n")));
        return Convert.ToHexString(bytes);
    }

    static List<string> DeJsonList(string json)
    {
        try { return JsonSerializer.Deserialize<List<string>>(string.IsNullOrWhiteSpace(json) ? "[]" : json) ?? new(); }
        catch { return new(); }
    }

    static string EnJsonList(List<string>? list) =>
        JsonSerializer.Serialize(list ?? new List<string>());

    static List<string>? NullIfEmpty(List<string> list) => list.Count == 0 ? null : list;

    CekaNaClovekaDto? DeCekaNaCloveka(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<CekaNaClovekaDto>(json, Json); }
        catch { return null; }
    }

    static DateTime? ParseDate(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        if (DateTime.TryParse(s, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var d))
            return DateTime.SpecifyKind(d, DateTimeKind.Utc);
        return null;
    }

    static string? FmtDate(DateTime? d) => d?.ToString("yyyy-MM-dd");
}
