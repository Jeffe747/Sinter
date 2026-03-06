using Microsoft.AspNetCore.DataProtection;

namespace SinterServer.Services;

public interface IGitCredentialProtector
{
    string Protect(string value);
    string Unprotect(string value);
}

public sealed class GitCredentialProtector(IDataProtectionProvider provider) : IGitCredentialProtector
{
    private readonly IDataProtector protector = provider.CreateProtector("SinterServer.GitCredential");

    public string Protect(string value) => protector.Protect(value);
    public string Unprotect(string value) => protector.Unprotect(value);
}