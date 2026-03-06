using SinterNode.Services;

namespace SinterNode.Tests;

public sealed class PublishedArtifactSelectorTests
{
    [Fact]
    public void SelectDllName_PrefersProjectNameThenAppNameThenSingleUserAssembly()
    {
        using var workspace = new TestWorkspace();
        var publishRoot = workspace.GetPath("publish");
        Directory.CreateDirectory(publishRoot);
        File.WriteAllText(Path.Combine(publishRoot, "HomeLab.Web.dll"), string.Empty);
        File.WriteAllText(Path.Combine(publishRoot, "System.Text.Json.dll"), string.Empty);

        var result = PublishedArtifactSelector.SelectDllName(publishRoot, "IgnoredAppName", "src/HomeLab.Web/HomeLab.Web.csproj");

        Assert.Equal("HomeLab.Web.dll", result);
    }
}