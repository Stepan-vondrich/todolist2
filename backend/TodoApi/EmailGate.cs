using System.Text;
using System.Text.Json;

namespace TodoApi;

// Restricts access to a single allowed e-mail using the identity Azure Container Apps' built-in
// auth (EasyAuth) forwards after a successful login: X-MS-CLIENT-PRINCIPAL-NAME (the e-mail) and
// X-MS-CLIENT-PRINCIPAL (base64 JSON of claims). EasyAuth sets these from the verified token and
// strips any client-supplied copies, so they can't be spoofed. Not tied to Google's "testing"
// restriction — only this exact address gets in, whatever the consent screen's publish status.
public static class EmailGate
{
    public static bool IsAuthorized(string? principalName, string? principalB64, string allowedEmail)
    {
        if (string.IsNullOrWhiteSpace(allowedEmail)) return false;
        var allowed = allowedEmail.Trim().ToLowerInvariant();

        if (!string.IsNullOrWhiteSpace(principalName) && principalName.Trim().ToLowerInvariant() == allowed)
            return true;

        if (!string.IsNullOrWhiteSpace(principalB64))
        {
            try
            {
                var json = Encoding.UTF8.GetString(Convert.FromBase64String(principalB64));
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("claims", out var claims) && claims.ValueKind == JsonValueKind.Array)
                    foreach (var c in claims.EnumerateArray())
                        if (c.TryGetProperty("val", out var v) && v.ValueKind == JsonValueKind.String
                            && string.Equals(v.GetString()?.Trim(), allowed, StringComparison.OrdinalIgnoreCase))
                            return true;
            }
            catch { /* malformed principal — deny */ }
        }
        return false;
    }
}
