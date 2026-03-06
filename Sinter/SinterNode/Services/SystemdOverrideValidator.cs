namespace SinterNode.Services;

public interface ISystemdOverrideValidator
{
    void Validate(string content);
    IReadOnlyList<string> GetWarnings(string content);
}

public sealed class SystemdOverrideValidator : ISystemdOverrideValidator
{
    private static readonly Dictionary<string, HashSet<string>> AllowedDirectives = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Service"] = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Environment",
            "EnvironmentFile",
            "WorkingDirectory",
            "ExecStart",
            "ExecStartPre",
            "ExecStartPost",
            "Restart",
            "RestartSec",
            "User",
            "Group",
            "KillSignal",
            "TimeoutStartSec",
            "TimeoutStopSec",
            "StandardOutput",
            "StandardError",
            "SyslogIdentifier"
        },
        ["Unit"] = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Description",
            "After",
            "Wants",
            "Requires"
        }
    };

    public void Validate(string content)
    {
        var warnings = new List<string>();
        Parse(content, warnings, throwOnError: true);
    }

    public IReadOnlyList<string> GetWarnings(string content)
    {
        var warnings = new List<string>();
        Parse(content, warnings, throwOnError: false);
        return warnings;
    }

    private static void Parse(string content, List<string> warnings, bool throwOnError)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return;
        }

        string? currentSection = null;
        var lineNumber = 0;

        foreach (var rawLine in content.Split('\n'))
        {
            lineNumber++;
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#') || line.StartsWith(';'))
            {
                continue;
            }

            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                currentSection = line[1..^1].Trim();
                if (!AllowedDirectives.ContainsKey(currentSection))
                {
                    Fail($"Unsupported systemd override section '{currentSection}' at line {lineNumber}.", throwOnError);
                }

                continue;
            }

            if (currentSection is null)
            {
                Fail($"systemd override content must declare a section before directives. Invalid line {lineNumber}.", throwOnError);
                continue;
            }

            var separatorIndex = line.IndexOf('=');
            if (separatorIndex <= 0)
            {
                Fail($"Invalid directive syntax at line {lineNumber}: '{line}'.", throwOnError);
                continue;
            }

            var directive = line[..separatorIndex].Trim();
            if (!AllowedDirectives[currentSection].Contains(directive))
            {
                Fail($"Directive '{directive}' is not allowed in [{currentSection}] overrides.", throwOnError);
                continue;
            }

            if (directive.Equals("ExecStart", StringComparison.OrdinalIgnoreCase) && !line.Contains("=", StringComparison.Ordinal))
            {
                warnings.Add($"ExecStart override at line {lineNumber} should be reviewed carefully.");
            }
        }
    }

    private static void Fail(string message, bool throwOnError)
    {
        if (throwOnError)
        {
            throw new InvalidOperationException(message);
        }
    }
}