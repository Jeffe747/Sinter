using System.Globalization;
using System.Net.NetworkInformation;
using Microsoft.Extensions.Options;
using SinterNode.Models;
using SinterNode.Options;

namespace SinterNode.Services;

public interface INodeTelemetryCollector
{
    Task<NodeTelemetry> CollectAsync(CancellationToken cancellationToken);
}

public sealed class NodeTelemetryCollector(IOptions<NodeOptions> options) : INodeTelemetryCollector
{
    public async Task<NodeTelemetry> CollectAsync(CancellationToken cancellationToken)
    {
        var loadAverage = await ReadLoadAverageAsync(cancellationToken);
        var cpuUsagePercent = await ReadCpuUsagePercentAsync(cancellationToken);
        var (memoryTotalBytes, memoryAvailableBytes) = await ReadMemoryAsync(cancellationToken);
        var memoryUsedPercent = CalculateUsedPercent(memoryTotalBytes, memoryAvailableBytes);
        var disk = ReadDisk(options.Value.ManagedAppsRoot);
        var diskUsedPercent = CalculateUsedPercent(disk.TotalBytes, disk.FreeBytes);
        var openPorts = ReadOpenPorts();

        return new NodeTelemetry(
            Environment.ProcessorCount,
            cpuUsagePercent,
            loadAverage.OneMinute,
            loadAverage.FiveMinute,
            loadAverage.FifteenMinute,
            memoryTotalBytes,
            memoryAvailableBytes,
            memoryUsedPercent,
            disk.MountPoint,
            disk.TotalBytes,
            disk.FreeBytes,
            diskUsedPercent,
            openPorts.Length,
            openPorts,
            BuildHealthSignals(cpuUsagePercent, loadAverage, memoryUsedPercent, diskUsedPercent, openPorts.Length));
    }

    private static async Task<(double? OneMinute, double? FiveMinute, double? FifteenMinute)> ReadLoadAverageAsync(CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsLinux() || !File.Exists("/proc/loadavg"))
        {
            return (null, null, null);
        }

        var raw = await File.ReadAllTextAsync("/proc/loadavg", cancellationToken);
        var parts = raw.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 3)
        {
            return (null, null, null);
        }

        return (
            ParseDouble(parts[0]),
            ParseDouble(parts[1]),
            ParseDouble(parts[2]));
    }

    private static async Task<double?> ReadCpuUsagePercentAsync(CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsLinux())
        {
            return null;
        }

        var first = await TryReadCpuSampleAsync(cancellationToken);
        if (first is null)
        {
            return null;
        }

        await Task.Delay(TimeSpan.FromMilliseconds(150), cancellationToken);

        var second = await TryReadCpuSampleAsync(cancellationToken);
        if (second is null)
        {
            return null;
        }

        var totalDelta = second.Value.Total - first.Value.Total;
        var idleDelta = second.Value.Idle - first.Value.Idle;
        if (totalDelta <= 0)
        {
            return null;
        }

        return Math.Clamp((1d - (double)idleDelta / totalDelta) * 100d, 0d, 100d);
    }

    private static async Task<(long? TotalBytes, long? AvailableBytes)> ReadMemoryAsync(CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsLinux() || !File.Exists("/proc/meminfo"))
        {
            return (null, null);
        }

        long? totalBytes = null;
        long? availableBytes = null;
        var lines = await File.ReadAllLinesAsync("/proc/meminfo", cancellationToken);
        foreach (var line in lines)
        {
            if (line.StartsWith("MemTotal:", StringComparison.Ordinal))
            {
                totalBytes = ParseMeminfoLine(line);
            }
            else if (line.StartsWith("MemAvailable:", StringComparison.Ordinal))
            {
                availableBytes = ParseMeminfoLine(line);
            }
        }

        return (totalBytes, availableBytes);
    }

    private static (string? MountPoint, long? TotalBytes, long? FreeBytes) ReadDisk(string preferredPath)
    {
        try
        {
            var fullPath = Path.GetFullPath(string.IsNullOrWhiteSpace(preferredPath) ? "/" : preferredPath);
            var candidate = DriveInfo.GetDrives()
                .Where(static drive => drive.IsReady)
                .OrderByDescending(static drive => drive.RootDirectory.FullName.Length)
                .FirstOrDefault(drive => fullPath.StartsWith(drive.RootDirectory.FullName, StringComparison.OrdinalIgnoreCase));

            candidate ??= DriveInfo.GetDrives().FirstOrDefault(static drive => drive.IsReady);
            if (candidate is null)
            {
                return (null, null, null);
            }

            return (candidate.RootDirectory.FullName, candidate.TotalSize, candidate.AvailableFreeSpace);
        }
        catch
        {
            return (null, null, null);
        }
    }

    private static string[] ReadOpenPorts()
    {
        try
        {
            var properties = IPGlobalProperties.GetIPGlobalProperties();
            var ports = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var endpoint in properties.GetActiveTcpListeners())
            {
                ports.Add($"{endpoint.Port}/tcp");
            }

            foreach (var endpoint in properties.GetActiveUdpListeners())
            {
                ports.Add($"{endpoint.Port}/udp");
            }

            return ports
                .OrderBy(static value => ParsePort(value))
                .ThenBy(static value => value, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch
        {
            return [];
        }
    }

    private static IReadOnlyList<string> BuildHealthSignals(
        double? cpuUsagePercent,
        (double? OneMinute, double? FiveMinute, double? FifteenMinute) loadAverage,
        double? memoryUsedPercent,
        double? diskUsedPercent,
        int openPortCount)
    {
        var signals = new List<string>();
        var cpuCount = Math.Max(1, Environment.ProcessorCount);

        if (cpuUsagePercent is >= 85d)
        {
            signals.Add("CPU usage is elevated.");
        }

        if (loadAverage.OneMinute is { } loadOneMinute && loadOneMinute / cpuCount >= 0.9d)
        {
            signals.Add("Load is near CPU capacity.");
        }

        if (loadAverage.OneMinute is { } oneMinute && loadAverage.FiveMinute is { } fiveMinute && oneMinute >= (fiveMinute * 1.35d) && oneMinute - fiveMinute >= 0.5d)
        {
            signals.Add("Load is climbing quickly.");
        }

        if (memoryUsedPercent is >= 90d)
        {
            signals.Add("Memory pressure is high.");
        }

        if (diskUsedPercent is >= 90d)
        {
            signals.Add("Disk space is running low.");
        }

        if (openPortCount >= 25)
        {
            signals.Add("Many ports are exposed.");
        }

        return signals;
    }

    private static async Task<(long Total, long Idle)?> TryReadCpuSampleAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists("/proc/stat"))
        {
            return null;
        }

        var lines = await File.ReadAllLinesAsync("/proc/stat", cancellationToken);
        var cpuLine = lines.FirstOrDefault(static line => line.StartsWith("cpu ", StringComparison.Ordinal));
        if (string.IsNullOrWhiteSpace(cpuLine))
        {
            return null;
        }

        var parts = cpuLine.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 5)
        {
            return null;
        }

        var values = parts.Skip(1)
            .Select(static part => long.TryParse(part, CultureInfo.InvariantCulture, out var value) ? value : -1L)
            .ToArray();
        if (values.Any(static value => value < 0))
        {
            return null;
        }

        var idle = values.Length > 3 ? values[3] : 0L;
        var ioWait = values.Length > 4 ? values[4] : 0L;
        return (values.Sum(), idle + ioWait);
    }

    private static double? ParseDouble(string raw)
    {
        return double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }

    private static long? ParseMeminfoLine(string line)
    {
        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2 || !long.TryParse(parts[1], CultureInfo.InvariantCulture, out var kilobytes))
        {
            return null;
        }

        return kilobytes * 1024L;
    }

    private static double? CalculateUsedPercent(long? totalBytes, long? availableBytes)
    {
        if (totalBytes is null || availableBytes is null || totalBytes <= 0)
        {
            return null;
        }

        return Math.Clamp(((double)(totalBytes.Value - availableBytes.Value) / totalBytes.Value) * 100d, 0d, 100d);
    }

    private static int ParsePort(string value)
    {
        var separatorIndex = value.IndexOf('/');
        return separatorIndex > 0 && int.TryParse(value[..separatorIndex], NumberStyles.Integer, CultureInfo.InvariantCulture, out var port)
            ? port
            : int.MaxValue;
    }
}