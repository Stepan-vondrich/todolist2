using System.Text;
using TodoApi;

namespace TodoApi.Tests;

public class BackupCryptoTests
{
    [Fact]
    public async Task DecryptToStream_RoundTripsEncrypt()
    {
        var plain = Encoding.UTF8.GetBytes("hello streaming backup — příliš žluťoučký kůň 12345");
        var enc = BackupCrypto.Encrypt(plain, "correct horse");

        using var input = new MemoryStream(enc);
        using var output = new MemoryStream();
        await BackupCrypto.DecryptToStreamAsync(input, "correct horse", output);

        Assert.Equal(plain, output.ToArray());
    }

    [Fact]
    public async Task DecryptToStream_RoundTripsLargeMultiBlockPayload()
    {
        var plain = new byte[257 * 1024]; // spans many AES blocks + non-block-aligned tail
        for (int i = 0; i < plain.Length; i++) plain[i] = (byte)(i * 31 + 7);
        var enc = BackupCrypto.Encrypt(plain, "pw");

        using var input = new MemoryStream(enc);
        using var output = new MemoryStream();
        await BackupCrypto.DecryptToStreamAsync(input, "pw", output);

        Assert.Equal(plain, output.ToArray());
    }

    [Fact]
    public async Task DecryptToStream_WrongPassword_DoesNotRecoverPlaintext()
    {
        var plain = Encoding.UTF8.GetBytes("secret-payload-should-not-leak");
        var enc = BackupCrypto.Encrypt(plain, "right-password");

        using var input = new MemoryStream(enc);
        using var output = new MemoryStream();
        try { await BackupCrypto.DecryptToStreamAsync(input, "wrong-password", output); }
        catch { return; } // threw on bad padding — correct outcome

        // If it didn't throw, the output must be garbage, never the real plaintext.
        Assert.NotEqual(plain, output.ToArray());
    }
}
