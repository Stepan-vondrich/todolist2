using System.Text;
using TodoApi;

namespace TodoApi.Tests;

public class BasicAuthTests
{
    static string Header(string u, string p) =>
        "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes($"{u}:{p}"));

    [Fact]
    public void IsAuthorized_CorrectCredentials_True()
        => Assert.True(BasicAuth.IsAuthorized(Header("Dabbox1", "s3cret"), "Dabbox1", "s3cret"));

    [Fact]
    public void IsAuthorized_WrongPassword_False()
        => Assert.False(BasicAuth.IsAuthorized(Header("Dabbox1", "nope"), "Dabbox1", "s3cret"));

    [Fact]
    public void IsAuthorized_WrongUser_False()
        => Assert.False(BasicAuth.IsAuthorized(Header("someone", "s3cret"), "Dabbox1", "s3cret"));

    [Fact]
    public void IsAuthorized_MissingHeader_False()
        => Assert.False(BasicAuth.IsAuthorized(null, "Dabbox1", "s3cret"));

    [Fact]
    public void IsAuthorized_NonBasicScheme_False()
        => Assert.False(BasicAuth.IsAuthorized("Bearer abc", "Dabbox1", "s3cret"));

    [Fact]
    public void IsAuthorized_GarbageBase64_False()
        => Assert.False(BasicAuth.IsAuthorized("Basic @@notbase64@@", "Dabbox1", "s3cret"));
}
