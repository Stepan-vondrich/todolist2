using Npgsql;

namespace TodoApi;

public static class PgConnection
{
    // Accepts either a postgres:// URI (Neon's default copy-paste form) or an already
    // Npgsql key-value connection string, and returns a key-value string Npgsql accepts.
    public static string Normalize(string conn)
    {
        if (string.IsNullOrWhiteSpace(conn)) return conn;
        if (!conn.StartsWith("postgres://") && !conn.StartsWith("postgresql://"))
            return conn; // already key-value form

        var uri = new Uri(conn);
        var userInfo = uri.UserInfo.Split(':', 2);
        var csb = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.Port > 0 ? uri.Port : 5432,
            Username = Uri.UnescapeDataString(userInfo[0]),
            Password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : null,
            Database = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/')),
            SslMode = SslMode.Require, // Neon requires TLS
        };
        return csb.ConnectionString;
    }
}
