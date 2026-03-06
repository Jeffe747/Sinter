namespace SinterNode.Tests;

public sealed class TestWorkspace : IDisposable
{
    public TestWorkspace()
    {
        Root = Path.Combine(Path.GetTempPath(), "sinter-node-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
    }

    public string Root { get; }

    public string GetPath(params string[] segments)
    {
        var allSegments = new string[segments.Length + 1];
        allSegments[0] = Root;
        Array.Copy(segments, 0, allSegments, 1, segments.Length);
        return Path.Combine(allSegments);
    }

    public void Dispose()
    {
        if (Directory.Exists(Root))
        {
            Directory.Delete(Root, recursive: true);
        }
    }
}