namespace TodoApi;

/// <summary>
/// Resolves the writable data directory for user data — the SQLite database and
/// uploaded comment attachments. Defaults to the app's base directory (so the
/// published Windows exe keeps its data next to itself), but can be redirected
/// with the DATA_DIR environment variable. In the container DATA_DIR points at a
/// mounted persistent volume (Azure Files) so data survives restarts/new revisions.
/// </summary>
public static class DataPaths
{
    public static string Root { get; } =
        Environment.GetEnvironmentVariable("DATA_DIR") is { Length: > 0 } d
            ? d
            : AppContext.BaseDirectory;

    public static string Uploads { get; } = Path.Combine(Root, "uploads");

    public static string Db { get; } = Path.Combine(Root, "todos.db");
}
