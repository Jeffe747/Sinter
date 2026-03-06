using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using SinterNode.Models;
using SinterNode.Options;

namespace SinterNode.Services;

public interface INodeStateStore
{
    Task<NodeStateSnapshot> GetSnapshotAsync(CancellationToken cancellationToken);
    Task<NodeStateSnapshot> UpdatePrefixesAsync(IReadOnlyCollection<string> prefixes, CancellationToken cancellationToken);
    Task<bool> ValidateApiKeyAsync(string? apiKey, CancellationToken cancellationToken);
}

public sealed class NodeStateStore(IOptions<NodeOptions> options, TimeProvider timeProvider) : INodeStateStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly SemaphoreSlim gate = new(1, 1);

    public async Task<NodeStateSnapshot> GetSnapshotAsync(CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            return await LoadOrCreateUnsafeAsync(cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<NodeStateSnapshot> UpdatePrefixesAsync(IReadOnlyCollection<string> prefixes, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            var snapshot = await LoadOrCreateUnsafeAsync(cancellationToken);
            var normalizedPrefixes = prefixes
                .Select(static prefix => prefix.Trim())
                .Where(static prefix => !string.IsNullOrWhiteSpace(prefix))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(static prefix => prefix, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var nextState = snapshot.State with
            {
                ServicePrefixes = normalizedPrefixes,
                UpdatedUtc = timeProvider.GetUtcNow(),
                BootstrapCompleted = normalizedPrefixes.Length > 0
            };

            await WriteStateUnsafeAsync(nextState, cancellationToken);
            return new NodeStateSnapshot(nextState, snapshot.ApiKey, ShowApiKey: !nextState.BootstrapCompleted);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<bool> ValidateApiKeyAsync(string? apiKey, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return false;
        }

        var snapshot = await GetSnapshotAsync(cancellationToken);
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(snapshot.ApiKey),
            Encoding.UTF8.GetBytes(apiKey.Trim()));
    }

    private async Task<NodeStateSnapshot> LoadOrCreateUnsafeAsync(CancellationToken cancellationToken)
    {
        EnsureParentDirectory(options.Value.StateFilePath);
        EnsureParentDirectory(options.Value.ApiKeyFilePath);

        if (!File.Exists(options.Value.StateFilePath) || !File.Exists(options.Value.ApiKeyFilePath))
        {
            var createdUtc = timeProvider.GetUtcNow();
            var initialState = new NodeState(
                Guid.NewGuid(),
                createdUtc,
                createdUtc,
                [],
                BootstrapCompleted: false);

            var generatedApiKey = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
            await WriteStateUnsafeAsync(initialState, cancellationToken);
            await File.WriteAllTextAsync(options.Value.ApiKeyFilePath, generatedApiKey, cancellationToken);
            return new NodeStateSnapshot(initialState, generatedApiKey, ShowApiKey: true);
        }

        var stateJson = await File.ReadAllTextAsync(options.Value.StateFilePath, cancellationToken);
        var persistedState = JsonSerializer.Deserialize<NodeState>(stateJson, SerializerOptions)
            ?? throw new InvalidOperationException("Node state file is invalid.");
        var persistedApiKey = (await File.ReadAllTextAsync(options.Value.ApiKeyFilePath, cancellationToken)).Trim();
        return new NodeStateSnapshot(persistedState, persistedApiKey, ShowApiKey: !persistedState.BootstrapCompleted);
    }

    private async Task WriteStateUnsafeAsync(NodeState state, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(state, SerializerOptions);
        await File.WriteAllTextAsync(options.Value.StateFilePath, json, cancellationToken);
    }

    private static void EnsureParentDirectory(string filePath)
    {
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }
}