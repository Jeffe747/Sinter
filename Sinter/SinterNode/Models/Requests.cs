namespace SinterNode.Models;

public sealed record UpdatePrefixesRequest(string[] Prefixes);

public sealed record UpdateServiceFileRequest(string? Content, bool AllowOverwriteUnmanaged = false, bool SkipValidation = false);

public sealed record DeployApplicationRequest(
    string RepoUrl,
    string AppName,
    string Branch = "master",
    string? Token = null,
    string? ProjectPath = null,
    string? ServiceName = null);

public sealed record SelfUpdateRequest(
    string RepoUrl,
    string Branch,
    string ProjectPath,
    string? Token);