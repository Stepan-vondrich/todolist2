using System.Security.Cryptography;
using System.Text;

namespace TodoApi;

// AES-CBC backup format: [salt(16)][iv(16)][PKCS7 ciphertext], key = PBKDF2(pw, salt).
public static class BackupCrypto
{
    const int SaltLen = 16, IvLen = 16, Iterations = 100_000;

    public static byte[] Encrypt(byte[] plaintext, string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltLen);
        var iv   = RandomNumberGenerator.GetBytes(IvLen);
        var key  = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, Iterations, HashAlgorithmName.SHA256, 32);
        using var aes = Aes.Create();
        aes.Key = key; aes.IV = iv;
        using var ms = new MemoryStream();
        ms.Write(salt); ms.Write(iv);
        using (var cs = new CryptoStream(ms, aes.CreateEncryptor(), CryptoStreamMode.Write))
            cs.Write(plaintext);
        return ms.ToArray();
    }

    // Streaming decrypt: reads the salt+iv header from `input`, then decrypts the rest into
    // `output` without buffering the whole payload — so a multi-hundred-MB backup imports
    // in a few MB of RAM instead of holding several full copies in memory.
    public static async Task DecryptToStreamAsync(Stream input, string password, Stream output)
    {
        var header = new byte[SaltLen + IvLen];
        await ReadExactAsync(input, header);
        var salt = header[..SaltLen];
        var iv   = header[SaltLen..];
        var key  = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, Iterations, HashAlgorithmName.SHA256, 32);
        using var aes = Aes.Create();
        aes.Key = key; aes.IV = iv;
        // leaveOpen: the caller owns `input` (e.g. the upload stream) and disposes it itself.
        await using var cs = new CryptoStream(input, aes.CreateDecryptor(), CryptoStreamMode.Read, leaveOpen: true);
        await cs.CopyToAsync(output);
    }

    static async Task ReadExactAsync(Stream s, byte[] buf)
    {
        int read = 0;
        while (read < buf.Length)
        {
            int n = await s.ReadAsync(buf.AsMemory(read));
            if (n == 0) throw new EndOfStreamException("Truncated backup: header shorter than expected.");
            read += n;
        }
    }
}
