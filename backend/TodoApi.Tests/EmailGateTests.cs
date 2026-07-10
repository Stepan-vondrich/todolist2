using System.Text;
using TodoApi;

namespace TodoApi.Tests;

public class EmailGateTests
{
    const string Allowed = "stepanvondrich@gmail.com";

    static string Principal(string email) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(
            $"{{\"auth_typ\":\"google\",\"claims\":[{{\"typ\":\"name\",\"val\":\"S\"}},{{\"typ\":\"email\",\"val\":\"{email}\"}}]}}"));

    [Fact]
    public void PrincipalNameMatches_Authorized()
        => Assert.True(EmailGate.IsAuthorized(Allowed, null, Allowed));

    [Fact]
    public void PrincipalNameCaseInsensitive_Authorized()
        => Assert.True(EmailGate.IsAuthorized("Stepan.Vondrich@GMAIL.com".Replace("Stepan.Vondrich", "stepanvondrich"), null, Allowed));

    [Fact]
    public void ClaimEmailMatches_Authorized()
        => Assert.True(EmailGate.IsAuthorized("Display Name", Principal(Allowed), Allowed));

    [Fact]
    public void DifferentEmail_NotAuthorized()
        => Assert.False(EmailGate.IsAuthorized("someone@else.com", Principal("someone@else.com"), Allowed));

    [Fact]
    public void NoHeaders_NotAuthorized()
        => Assert.False(EmailGate.IsAuthorized(null, null, Allowed));

    [Fact]
    public void NoAllowedEmail_NotAuthorized()
        => Assert.False(EmailGate.IsAuthorized(Allowed, Principal(Allowed), ""));
}
