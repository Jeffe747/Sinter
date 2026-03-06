namespace SinterNode.Models;

public sealed record OperationEvent(
    string Type,
    string Message,
    DateTimeOffset TimestampUtc,
    string? Scope = null,
    string? Command = null,
    int? ExitCode = null)
{
    public static OperationEvent Info(string message, string? scope = null) => new("info", message, DateTimeOffset.UtcNow, scope);
    public static OperationEvent Warning(string message, string? scope = null) => new("warning", message, DateTimeOffset.UtcNow, scope);
    public static OperationEvent Error(string message, string? scope = null, int? exitCode = null) => new("error", message, DateTimeOffset.UtcNow, scope, null, exitCode);
    public static OperationEvent Success(string message, string? scope = null) => new("success", message, DateTimeOffset.UtcNow, scope);
    public static OperationEvent CommandOutput(string message, string command, string? scope = null) => new("command", message, DateTimeOffset.UtcNow, scope, command);
}