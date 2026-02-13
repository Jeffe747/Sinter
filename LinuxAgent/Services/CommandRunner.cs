using System.Diagnostics;
using System.Text;
using System.Threading.Channels;

namespace LinuxAgent.Services;

public interface ICommandRunner
{
    Task<CommandResult> RunAsync(string command, string args, string workingDir = "/");
    IAsyncEnumerable<string> StreamAsync(string command, string args, string workingDir = "/");
}

public record CommandResult(int ExitCode, string StdOut, string StdErr);

public class CommandRunner : ICommandRunner
{
    private readonly ILogger<CommandRunner> _logger;

    public CommandRunner(ILogger<CommandRunner> logger)
    {
        _logger = logger;
    }

    public async Task<CommandResult> RunAsync(string command, string args, string workingDir = "/")
    {
        _logger.LogInformation("Executing: {Command} {Args} in {Dir}", command, args, workingDir);
        
        var startInfo = new ProcessStartInfo
        {
            FileName = command,
            Arguments = args,
            WorkingDirectory = workingDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = startInfo };
        var stdOut = new StringBuilder();
        var stdErr = new StringBuilder();

        process.OutputDataReceived += (_, e) => { if (e.Data != null) stdOut.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data != null) stdErr.AppendLine(e.Data); };

        try
        {
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            await process.WaitForExitAsync();

            return new CommandResult(process.ExitCode, stdOut.ToString(), stdErr.ToString());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to execute command");
            return new CommandResult(-1, string.Empty, ex.Message);
        }
    }

    public async IAsyncEnumerable<string> StreamAsync(string command, string args, string workingDir = "/")
    {
        _logger.LogInformation("Streaming: {Command} {Args} in {Dir}", command, args, workingDir);

        var startInfo = new ProcessStartInfo
        {
            FileName = command,
            Arguments = args,
            WorkingDirectory = workingDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = startInfo };
        var channel = Channel.CreateUnbounded<string>();

        process.OutputDataReceived += async (_, e) => { 
            if (e.Data != null) await channel.Writer.WriteAsync($"[INFO] {e.Data}"); 
        };
        process.ErrorDataReceived += async (_, e) => { 
            if (e.Data != null) await channel.Writer.WriteAsync($"[ERR]  {e.Data}"); 
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        // Background waiter to close channel
        _ = Task.Run(async () => {
             try {
                await process.WaitForExitAsync();
             } finally {
                channel.Writer.TryComplete();
             }
        });

        await foreach (var line in channel.Reader.ReadAllAsync())
        {
            yield return line;
        }

        if (process.ExitCode != 0)
        {
            yield return $"[FAIL] Process exited with code {process.ExitCode}";
        }
        else 
        {
             yield return $"[DONE] Process finished successfully.";
        }
    }
}
