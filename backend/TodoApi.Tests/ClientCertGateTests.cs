using TodoApi;

namespace TodoApi.Tests;

public class ClientCertGateTests
{
    const string Pin = "aabbccddeeff00112233445566778899aabbccddeeff00112233445566778899";

    [Fact]
    public void MatchingHash_Authorized()
        => Assert.True(ClientCertGate.IsAuthorized($"Hash={Pin};Subject=\"CN=x\"", Pin));

    [Fact]
    public void UppercaseOrColonHash_Authorized()
        => Assert.True(ClientCertGate.IsAuthorized($"Hash=\"{Pin.ToUpperInvariant()}\"", Pin));

    [Fact]
    public void WrongHash_NotAuthorized()
        => Assert.False(ClientCertGate.IsAuthorized("Hash=deadbeef", Pin));

    [Fact]
    public void NoHeader_NotAuthorized()
        => Assert.False(ClientCertGate.IsAuthorized(null, Pin));

    [Fact]
    public void NoPin_NotAuthorized()
        => Assert.False(ClientCertGate.IsAuthorized($"Hash={Pin}", ""));

    [Fact]
    public void NoHashField_NotAuthorized()
        => Assert.False(ClientCertGate.IsAuthorized("Subject=\"CN=x\";By=spiffe://y", Pin));
}
