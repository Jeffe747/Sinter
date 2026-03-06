using Microsoft.AspNetCore.DataProtection;
using SinterServer.Services;

namespace SinterServer.Tests;

public sealed class CredentialProtectorTests
{
    [Fact]
    public void ProtectAndUnprotect_RoundTripsSecret()
    {
        var provider = DataProtectionProvider.Create("sinter-server-tests");
        var protector = new GitCredentialProtector(provider);

        var encrypted = protector.Protect("ghp_secret_token");

        Assert.NotEqual("ghp_secret_token", encrypted);
        Assert.Equal("ghp_secret_token", protector.Unprotect(encrypted));
    }
}