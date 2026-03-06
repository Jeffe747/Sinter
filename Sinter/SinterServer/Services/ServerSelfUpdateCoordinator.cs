using System.Diagnostics;
using Microsoft.Extensions.Options;
using SinterServer.Models;
using SinterServer.Options;

namespace SinterServer.Services;

public interface IServerSelfUpdateCoordinator
{
    Task<RemoteActionResult> StartAsync(SelfUpdateRequest request, CancellationToken cancellationToken);
}

public sealed class ServerSelfUpdateCoordinator(IOptions<SinterServerOptions> options) : IServerSelfUpdateCoordinator
{
    public async Task<RemoteActionResult> StartAsync(SelfUpdateRequest request, CancellationToken cancellationToken)
    {
        var scriptPath = options.Value.SelfUpdateScriptPath;
        var logPath = options.Value.SelfUpdateLogPath;
        var repoUrl = EscapeSingleQuoted(request.RepoUrl);
        var branch = EscapeSingleQuoted(request.Branch);
        var command = $"nohup '{EscapeSingleQuoted(scriptPath)}' --repo-url '{repoUrl}' --branch '{branch}' > '{EscapeSingleQuoted(logPath)}' 2>&1 &";
        var events = new List<RemoteEvent>
        {
            new("info", $"Handing off self-update to {scriptPath}.", DateTimeOffset.UtcNow, "self-update"),
            new("info", $"Self-update log will be written to {logPath}.", DateTimeOffset.UtcNow, "self-update")
        };

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "/bin/bash",
                Arguments = $"-lc \"{command}\"",
                WorkingDirectory = "/",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.Start();
        var stdOutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stdErrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        var standardError = await stdErrTask;
        _ = await stdOutTask;

        if (process.ExitCode != 0)
        {
            var summary = $"Failed to start the updater script: {standardError}".Trim();
            events.Add(new RemoteEvent("error", summary, DateTimeOffset.UtcNow, "self-update", process.ExitCode));
            return new RemoteActionResult("Error", summary, events);
        }

        const string successSummary = "Self-update handoff completed. The server service will restart if the updater succeeds.";
        events.Add(new RemoteEvent("success", successSummary, DateTimeOffset.UtcNow, "self-update"));
        return new RemoteActionResult("Success", successSummary, events);
    }

    private static string EscapeSingleQuoted(string value)
    {
        return value.Replace("'", "'\\''", StringComparison.Ordinal);
    }
}