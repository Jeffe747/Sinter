using System.Diagnostics;
using System.Text;

namespace SinterNode.Services;

public interface IProcessRunner
{
    Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken cancellationToken);
    IAsyncEnumerable<ProcessOutputLine> StreamAsync(ProcessRequest request, CancellationToken cancellationToken);
}

public sealed record ProcessRequest(string FileName, string Arguments, string WorkingDirectory);

public sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);

public sealed record ProcessOutputLine(string Text, bool IsError, bool IsTerminal = false, int? ExitCode = null);

public sealed class ProcessRunner(ILogger<ProcessRunner> logger) : IProcessRunner
{
    public async Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken cancellationToken)
    {
        logger.LogInformation("Running command {Command} {Arguments} in {WorkingDirectory}", request.FileName, request.Arguments, request.WorkingDirectory);

        using var process = CreateProcess(request);
        var stdOut = new StringBuilder();
        var stdErr = new StringBuilder();

        process.OutputDataReceived += (_, args) =>
        {
            if (args.Data is not null)
            {
                stdOut.AppendLine(args.Data);
            }
        };

        process.ErrorDataReceived += (_, args) =>
        {
            if (args.Data is not null)
            {
                stdErr.AppendLine(args.Data);
            }
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await process.WaitForExitAsync(cancellationToken);

        return new ProcessResult(process.ExitCode, stdOut.ToString(), stdErr.ToString());
    }

    public async IAsyncEnumerable<ProcessOutputLine> StreamAsync(ProcessRequest request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        logger.LogInformation("Streaming command {Command} {Arguments} in {WorkingDirectory}", request.FileName, request.Arguments, request.WorkingDirectory);

        using var process = CreateProcess(request);
        process.Start();

        var stdOutTask = ReadLinesAsync(process.StandardOutput, isError: false, cancellationToken);
        var stdErrTask = ReadLinesAsync(process.StandardError, isError: true, cancellationToken);
        var pending = new List<Task<IReadOnlyList<ProcessOutputLine>>> { stdOutTask, stdErrTask };

        while (pending.Count > 0)
        {
            var completed = await Task.WhenAny(pending);
            pending.Remove(completed);
            var lines = await completed;
            foreach (var line in lines)
            {
                yield return line;
            }
        }

        await process.WaitForExitAsync(cancellationToken);
        yield return new ProcessOutputLine($"Process exited with code {process.ExitCode}.", process.ExitCode != 0, IsTerminal: true, ExitCode: process.ExitCode);
    }

    private static Process CreateProcess(ProcessRequest request)
    {
        return new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = request.FileName,
                Arguments = request.Arguments,
                WorkingDirectory = request.WorkingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };
    }

    private static async Task<IReadOnlyList<ProcessOutputLine>> ReadLinesAsync(StreamReader reader, bool isError, CancellationToken cancellationToken)
    {
        var lines = new List<ProcessOutputLine>();
        while (true)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (line is null)
            {
                break;
            }

            if (line.Length > 0)
            {
                lines.Add(new ProcessOutputLine(line, isError));
            }
        }

        return lines;
    }
}