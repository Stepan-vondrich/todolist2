using System.Text;

namespace TodoApi;

public static class BasicAuth
{
    // True if the HTTP Authorization header is a valid "Basic base64(user:pass)" that
    // matches the expected credentials. Any missing/malformed/mismatched header -> false.
    public static bool IsAuthorized(string? authHeader, string user, string pass)
    {
        if (string.IsNullOrEmpty(authHeader) ||
            !authHeader.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
            return false;
        try
        {
            var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(authHeader["Basic ".Length..].Trim()));
            var sep = decoded.IndexOf(':');
            if (sep < 0) return false;
            return decoded[..sep] == user && decoded[(sep + 1)..] == pass;
        }
        catch { return false; }
    }
}
