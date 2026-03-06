using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace SinterServer.Tests;

public sealed class SinterServerFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "sinter-server-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        builder.UseEnvironment("Development");
        builder.UseSetting("SinterServer:DatabasePath", Path.Combine(tempRoot, "server.db"));
        builder.UseSetting("SinterServer:PollIntervalSeconds", "300");
        builder.UseSetting("SinterServer:ServerName", "SinterServer Test");
    }
}