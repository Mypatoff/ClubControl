using ClubControl.Shared.Security;
using Xunit;

namespace ClubControl.Tests;

public class PcTokenHasherTests
{
    [Fact]
    public void GenerateToken_ProducesDifferentTokensEachTime()
    {
        var a = PcTokenHasher.GenerateToken();
        var b = PcTokenHasher.GenerateToken();

        Assert.NotEqual(a, b);
        Assert.NotEmpty(a);
    }

    [Fact]
    public void Hash_ThenVerify_WithTheSameToken_Succeeds()
    {
        var token = PcTokenHasher.GenerateToken();
        var hash = PcTokenHasher.Hash(token);

        Assert.True(PcTokenHasher.Verify(token, hash));
    }

    [Fact]
    public void Verify_WithADifferentToken_Fails()
    {
        var hash = PcTokenHasher.Hash(PcTokenHasher.GenerateToken());
        var wrongToken = PcTokenHasher.GenerateToken();

        Assert.False(PcTokenHasher.Verify(wrongToken, hash));
    }

    [Fact]
    public void Hash_NeverStoresThePlaintextToken()
    {
        var token = PcTokenHasher.GenerateToken();
        var hash = PcTokenHasher.Hash(token);

        Assert.NotEqual(token, hash);
    }

    [Fact]
    public void Verify_WithACorruptedStoredHash_FailsInsteadOfThrowing()
    {
        Assert.False(PcTokenHasher.Verify("whatever", "not-valid-base64!!"));
    }

    [Fact]
    public void Verify_WithAnEmptyStoredHash_Fails()
    {
        Assert.False(PcTokenHasher.Verify("whatever", ""));
    }

    // ---- IsHeartbeatAuthorized: the decision ClubHub.Heartbeat makes about a claimed MachineGuid ----

    [Fact]
    public void IsHeartbeatAuthorized_BrandNewOrUnprovisionedPc_TrustsAnyCaller()
    {
        // No stored token yet (new PC, or configured but not yet provisioned) - legitimate
        // first-time setup must not be blocked by requiring a token nobody has been given yet.
        Assert.True(PcTokenHasher.IsHeartbeatAuthorized(storedTokenHash: null, presentedToken: null));
        Assert.True(PcTokenHasher.IsHeartbeatAuthorized(storedTokenHash: null, presentedToken: "anything"));
    }

    [Fact]
    public void IsHeartbeatAuthorized_ProvisionedPc_LegitimateReconnect_Succeeds()
    {
        var token = PcTokenHasher.GenerateToken();
        var storedHash = PcTokenHasher.Hash(token);

        Assert.True(PcTokenHasher.IsHeartbeatAuthorized(storedHash, token));
    }

    [Fact]
    public void IsHeartbeatAuthorized_ProvisionedPc_NoToken_IsRejected()
    {
        // The impersonation case this whole mechanism exists to stop: an attacker who only knows
        // the MachineGuid (e.g. sniffed off the LAN), with no token, must not be trusted.
        var storedHash = PcTokenHasher.Hash(PcTokenHasher.GenerateToken());

        Assert.False(PcTokenHasher.IsHeartbeatAuthorized(storedHash, presentedToken: null));
    }

    [Fact]
    public void IsHeartbeatAuthorized_ProvisionedPc_WrongToken_IsRejected()
    {
        var storedHash = PcTokenHasher.Hash(PcTokenHasher.GenerateToken());
        var wrongToken = PcTokenHasher.GenerateToken();

        Assert.False(PcTokenHasher.IsHeartbeatAuthorized(storedHash, wrongToken));
    }
}
