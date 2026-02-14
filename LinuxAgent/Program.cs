using LinuxAgent.Auth;
using LinuxAgent.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<IIPLockoutService, IPLockoutService>();
builder.Services.AddSingleton<ICommandRunner, CommandRunner>();
builder.Services.AddSingleton<DeploymentService>();

var app = builder.Build();

// Middleware
// Configure forwarded headers for proxy support (YARP, Cloudflare)
var forwardedHeadersOptions = new ForwardedHeadersOptions
{
    ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor | Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto
};
// Trust all proxies (Use only behind a firewall/YARP!)
forwardedHeadersOptions.KnownIPNetworks.Clear();
forwardedHeadersOptions.KnownProxies.Clear();
app.UseForwardedHeaders(forwardedHeadersOptions);

// Enable static files (for dashboard)
app.UseDefaultFiles();
app.UseStaticFiles();

// Middleware that requires Auth (API only, excluding status and dashboard)
app.UseWhen(context => 
    context.Request.Path.StartsWithSegments("/api") && 
    !context.Request.Path.StartsWithSegments("/api/dashboard") &&
    !context.Request.Path.StartsWithSegments("/api/status"), 
    appBuilder =>
{
    appBuilder.UseMiddleware<ApiKeyAuthMiddleware>();
});

// Endpoints
app.MapGet("/health", () => Results.Ok("OK")); // Standard health check for YARP

app.MapGet("/api/dashboard", () => 
{
    var process = System.Diagnostics.Process.GetCurrentProcess();
    var uptime = DateTime.UtcNow - process.StartTime.ToUniversalTime();
    
    var appsDir = "/opt/linux-agent/apps";
    var apps = Directory.Exists(appsDir) 
        ? Directory.GetDirectories(appsDir).Select(Path.GetFileName).ToArray() 
        : Array.Empty<string>();

    return Results.Ok(new 
    {
        Hostname = System.Net.Dns.GetHostName(),
        OsDescription = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
        Uptime = $"{uptime.Days}d {uptime.Hours}h {uptime.Minutes}m",
        MemoryUsage = $"{process.WorkingSet64 / 1024 / 1024} MB",
        Apps = apps
    });
});

app.MapPost("/api/deploy", async (HttpContext context, DeploymentService deployer) =>
{
    // We expect JSON body, but we need to stream the response.
    // Reading body before writing response.
    var req = await context.Request.ReadFromJsonAsync<DeployRequest>();
    if (req == null) 
    {
        context.Response.StatusCode = 400;
        await context.Response.WriteAsync("Invalid request body");
        return;
    }

    context.Response.ContentType = "text/plain";
    
    await foreach (var log in deployer.DeployAsync(req.RepoUrl, req.AppName, req.Branch, req.Token, req.DryRun))
    {
        await context.Response.WriteAsync(log + "\n");
        await context.Response.Body.FlushAsync();
    }
});

app.MapGet("/api/logs", async (HttpContext context) =>
{
    var lines = context.Request.Query["lines"].ToString();
    if (string.IsNullOrEmpty(lines)) lines = "100";

    // Use journalctl to get logs for the service
    var process = new System.Diagnostics.Process
    {
        StartInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "journalctl",
            Arguments = $"-u linux-agent -n {lines} --no-pager",
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        }
    };
    process.Start();
    var output = await process.StandardOutput.ReadToEndAsync();
    await process.WaitForExitAsync();

    return Results.Text(output);
});

app.MapPost("/api/update", () =>
{
    // Trigger update script in background
    Task.Run(() =>
    {
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = "/bin/bash",
            Arguments = "/opt/linux-agent/update.sh",
            UseShellExecute = true,
            CreateNoWindow = true
        });
    });
    return Results.Accepted(value: "Update started. Service will restart shortly.");
});


app.MapPost("/api/system/install-libs", async (ICommandRunner runner, InstallLibsRequest req) =>
{
    var packages = string.Join(" ", req.Packages);
    var result = await runner.RunAsync("apt-get", $"install -y {packages}");
    return result.ExitCode == 0 ? Results.Ok(result) : Results.BadRequest(result);
});

app.MapPost("/api/system/open-port", async (ICommandRunner runner, OpenPortRequest req) =>
{
    var result = await runner.RunAsync("ufw", $"allow {req.Port}/{req.Protocol}");
    return result.ExitCode == 0 ? Results.Ok(result) : Results.BadRequest(result);
});

app.MapGet("/api/status", () => Results.Ok(new { Status = "Online", Version = "1.1.0" }));

app.Run();

// DTOs
public record DeployRequest(string RepoUrl, string AppName, string Branch = "main", string? Token = null, bool DryRun = false);
public record InstallLibsRequest(string[] Packages);
public record OpenPortRequest(int Port, string Protocol = "tcp");
