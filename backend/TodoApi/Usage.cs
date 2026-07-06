using System.Text.Json;

namespace TodoApi;

public static class Usage
{
    // Compressed image size from an OCI/Docker v2 manifest: config blob + all layers.
    public static long ManifestTotalBytes(string manifestJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(manifestJson);
            var root = doc.RootElement;
            long total = 0;
            if (root.TryGetProperty("config", out var cfg) && cfg.TryGetProperty("size", out var cs))
                total += cs.GetInt64();
            if (root.TryGetProperty("layers", out var layers) && layers.ValueKind == JsonValueKind.Array)
                foreach (var l in layers.EnumerateArray())
                    if (l.TryGetProperty("size", out var ls)) total += ls.GetInt64();
            return total;
        }
        catch { return 0; }
    }

    // Total size (bytes) and file count under a directory, recursively. Missing dir -> (0,0).
    public static (long bytes, int count) DirectorySize(string path)
    {
        if (!Directory.Exists(path)) return (0, 0);
        long bytes = 0;
        int count = 0;
        foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
        {
            try { bytes += new FileInfo(file).Length; count++; }
            catch { /* skip files we can't stat */ }
        }
        return (bytes, count);
    }
}
