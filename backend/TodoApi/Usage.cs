namespace TodoApi;

public static class Usage
{
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
