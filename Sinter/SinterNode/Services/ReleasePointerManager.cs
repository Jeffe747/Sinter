namespace SinterNode.Services;

public interface IReleasePointerManager
{
    Task<string?> GetCurrentTargetAsync(string pointerPath, CancellationToken cancellationToken);
    Task PointToAsync(string pointerPath, string targetPath, CancellationToken cancellationToken);
}

public sealed class SymlinkReleasePointerManager : IReleasePointerManager
{
    public Task<string?> GetCurrentTargetAsync(string pointerPath, CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        if (!Directory.Exists(pointerPath))
        {
            return Task.FromResult<string?>(null);
        }

        var directoryInfo = new DirectoryInfo(pointerPath);
        var target = directoryInfo.ResolveLinkTarget(returnFinalTarget: true)?.FullName;
        return Task.FromResult(target);
    }

    public Task PointToAsync(string pointerPath, string targetPath, CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        Directory.CreateDirectory(Path.GetDirectoryName(pointerPath)!);

        if (Directory.Exists(pointerPath))
        {
            Directory.Delete(pointerPath);
        }
        else if (File.Exists(pointerPath))
        {
            File.Delete(pointerPath);
        }

        Directory.CreateSymbolicLink(pointerPath, targetPath);
        return Task.CompletedTask;
    }
}