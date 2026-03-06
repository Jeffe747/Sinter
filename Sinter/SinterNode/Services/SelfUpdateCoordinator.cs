using Microsoft.Extensions.Options;
using SinterNode.Models;
using SinterNode.Options;

namespace SinterNode.Services;

public interface ISelfUpdateCoordinator
{
    IAsyncEnumerable<OperationEvent> StartAsync(SelfUpdateRequest request, CancellationToken cancellationToken);
}

public sealed class SelfUpdateCoordinator(IOptions<NodeOptions> options, IProcessRunner processRunner) : ISelfUpdateCoordinator
{
    public async IAsyncEnumerable<OperationEvent> StartAsync(SelfUpdateRequest request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var scriptPath = options.Value.SelfUpdateScriptPath;
        var logPath = options.Value.SelfUpdateLogPath;
        var repoUrl = EscapeSingleQuoted(InjectToken(request.RepoUrl, request.Token));
        var branch = EscapeSingleQuoted(request.Branch);
        var command = $"nohup '{EscapeSingleQuoted(scriptPath)}' --repo-url '{repoUrl}' --branch '{branch}' > '{EscapeSingleQuoted(logPath)}' 2>&1 &";

        yield return OperationEvent.Info($"Handing off self-update to {scriptPath}.", "self-update");
        yield return OperationEvent.Info($"Self-update log will be written to {logPath}.", "self-update");

        var result = await processRunner.RunAsync(new ProcessRequest("/bin/bash", $"-lc \"{command}\"", "/"), cancellationToken);
        if (result.ExitCode != 0)
        {
            yield return OperationEvent.Error($"Failed to start the updater script: {result.StandardError}".Trim(), "self-update", result.ExitCode);
            yield break;
        }

        yield return OperationEvent.Success("Self-update handoff completed. The node service will restart if the updater succeeds.", "self-update");
    }

    private static string InjectToken(string repoUrl, string? token)
    {
        if (string.IsNullOrWhiteSpace(token) || !repoUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return repoUrl;
        }

        return repoUrl.Insert("https://".Length, $"oauth2:{token}@");
    }

    private static string EscapeSingleQuoted(string value)
    {
        return value.Replace("'", "'\\''", StringComparison.Ordinal);
    }
}