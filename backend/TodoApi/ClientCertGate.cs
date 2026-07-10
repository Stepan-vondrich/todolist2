using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace TodoApi;

// Validates the caller's client certificate from Envoy's X-Forwarded-Client-Cert (XFCC) header,
// which Azure Container Apps adds when ingress client-certificate mode is accept/require. Access
// is granted only when the presented cert's SHA-256 thumbprint matches the pinned one — so only
// devices holding our private key/cert get in, regardless of the password.
public static class ClientCertGate
{
    public static bool IsAuthorized(string? xfcc, string pinnedSha256)
    {
        if (string.IsNullOrWhiteSpace(xfcc) || string.IsNullOrWhiteSpace(pinnedSha256)) return false;
        var pinned = Normalize(pinnedSha256);

        // XFCC is a list of key=value fields (separated by ';' within a cert, ',' between certs).
        // Prefer the Hash field (SHA-256 of the DER cert); fall back to hashing the Cert PEM.
        foreach (var part in xfcc.Split(';', ','))
        {
            var eq = part.IndexOf('=');
            if (eq < 0) continue;
            var key = part[..eq].Trim();
            var val = part[(eq + 1)..].Trim().Trim('"');

            if (key.Equals("Hash", StringComparison.OrdinalIgnoreCase))
            {
                if (Normalize(val) == pinned) return true;
            }
            else if (key.Equals("Cert", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var pem = Uri.UnescapeDataString(val);
                    using var cert = X509Certificate2.CreateFromPem(pem);
                    var fp = Convert.ToHexString(cert.GetCertHash(HashAlgorithmName.SHA256));
                    if (Normalize(fp) == pinned) return true;
                }
                catch { /* malformed cert value — ignore */ }
            }
        }
        return false;
    }

    static string Normalize(string s) => s.Replace(":", "").Replace(" ", "").Trim().ToLowerInvariant();
}
