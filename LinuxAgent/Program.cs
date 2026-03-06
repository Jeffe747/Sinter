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

    if (!AgentValidation.IsValidAppName(req.AppName))
    {
        context.Response.StatusCode = 400;
        await context.Response.WriteAsync("Invalid appName. Allowed characters: letters, numbers, dot, dash, underscore.");
        return;
    }

    context.Response.ContentType = "text/plain";
    
    await foreach (var log in deployer.DeployAsync(req.RepoUrl, req.AppName, req.Branch, req.Token, req.DryRun, req.ProjectPath))
    {
        await context.Response.WriteAsync(log + "\n");
        await context.Response.Body.FlushAsync();
    }
});

app.MapPost("/api/redeploy/{appName}", async (HttpContext context, string appName, DeploymentService deployer) =>
{
    if (!AgentValidation.IsValidAppName(appName))
    {
        context.Response.StatusCode = 400;
        await context.Response.WriteAsync("[ERR] Invalid app name.\n");
        return;
    }

    var baseDir = $"/opt/linux-agent/apps/{appName}";
    var deployJsonPath = Path.Combine(baseDir, "deploy.json");

    if (!File.Exists(deployJsonPath))
    {
        context.Response.StatusCode = 404;
        await context.Response.WriteAsync($"[ERR] No deployment metadata found for {appName}. Cannot redeploy.\n");
        return;
    }

    DeployRequest? req = null;
    try
    {
        var json = await File.ReadAllTextAsync(deployJsonPath);
        req = System.Text.Json.JsonSerializer.Deserialize<DeployRequest>(json, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
    }
    catch (Exception ex)
    {
        context.Response.StatusCode = 500;
        await context.Response.WriteAsync($"[ERR] Failed to read deployment metadata: {ex.Message}\n");
        return;
    }

    if (req == null)
    {
        context.Response.StatusCode = 500;
        await context.Response.WriteAsync($"[ERR] Invalid deployment metadata.\n");
        return;
    }

    context.Response.ContentType = "text/plain";
    
    await foreach (var log in deployer.DeployAsync(req.RepoUrl, req.AppName, req.Branch, req.Token, req.DryRun, req.ProjectPath))
    {
        await context.Response.WriteAsync(log + "\n");
        await context.Response.Body.FlushAsync();
    }
});

app.MapPost("/api/delete/{appName}", async (HttpContext context, string appName, DeploymentService deployer) =>
{
    if (!AgentValidation.IsValidAppName(appName))
    {
        context.Response.StatusCode = 400;
        await context.Response.WriteAsync("[ERR] Invalid app name.\n");
        return;
    }

    var baseDir = $"/opt/linux-agent/apps/{appName}";
    var servicePath = $"/etc/systemd/system/{appName}.service";
    if (!Directory.Exists(baseDir) && !File.Exists(servicePath))
    {
        context.Response.StatusCode = 404;
        await context.Response.WriteAsync($"[ERR] App '{appName}' was not found on this machine.\n");
        return;
    }

    context.Response.ContentType = "text/plain";
    await foreach (var log in deployer.RemoveDeploymentAsync(appName))
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
            Arguments = Path.Combine(AppContext.BaseDirectory, "update.sh"),
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

app.MapGet("/api/systemd/override/{serviceName}", async (string serviceName) =>
{
    if (!AgentValidation.IsValidServiceName(serviceName))
    {
        return Results.BadRequest("Invalid service name. Allowed characters: letters, numbers, dot, dash, underscore.");
    }

    var overridePath = $"/etc/systemd/system/{serviceName}.service.d/override.conf";
    var exists = File.Exists(overridePath);
    var content = exists
        ? await File.ReadAllTextAsync(overridePath)
        : "[Service]\n# Add or override service settings here\n";

    return Results.Ok(new
    {
        ServiceName = serviceName,
        Exists = exists,
        Content = content
    });
});

app.MapPost("/api/systemd/override/{serviceName}", async (string serviceName, SystemdOverrideRequest req, ICommandRunner runner) =>
{
    if (!AgentValidation.IsValidServiceName(serviceName))
    {
        return Results.BadRequest("Invalid service name. Allowed characters: letters, numbers, dot, dash, underscore.");
    }

    if (req is null || string.IsNullOrWhiteSpace(req.Content))
    {
        return Results.BadRequest("Override content is required.");
    }

    if (req.Content.Length > 64_000)
    {
        return Results.BadRequest("Override file is too large.");
    }

    var dropInDir = $"/etc/systemd/system/{serviceName}.service.d";
    var overridePath = Path.Combine(dropInDir, "override.conf");

    try
    {
        Directory.CreateDirectory(dropInDir);
        var normalized = req.Content.Replace("\r\n", "\n");
        if (!normalized.EndsWith("\n"))
        {
            normalized += "\n";
        }

        await File.WriteAllTextAsync(overridePath, normalized);

        var reload = await runner.RunAsync("systemctl", "daemon-reload");
        if (reload.ExitCode != 0)
        {
            return Results.BadRequest(new
            {
                Error = "Failed to reload systemd daemon.",
                reload.StdErr,
                reload.StdOut
            });
        }

        var restart = await runner.RunAsync("systemctl", $"restart {serviceName}");
        if (restart.ExitCode != 0)
        {
            return Results.BadRequest(new
            {
                Error = "Override saved, but service restart failed.",
                restart.StdErr,
                restart.StdOut
            });
        }

        return Results.Ok(new
        {
            Message = $"Override saved and service '{serviceName}' restarted.",
            Path = overridePath
        });
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new
        {
            Error = "Failed to save override.",
            ex.Message
        });
    }
});

app.MapGet("/api/status", () => Results.Ok(new { Status = "Online", Version = "1.1.0" }));

app.Run();

// DTOs
public record DeployRequest(string RepoUrl, string AppName, string Branch = "main", string? Token = null, bool DryRun = false, string? ProjectPath = null);
public record InstallLibsRequest(string[] Packages);
public record OpenPortRequest(int Port, string Protocol = "tcp");
public record SystemdOverrideRequest(string Content);

public static class AgentValidation
{
    public static bool IsValidAppName(string appName)
    {
        if (string.IsNullOrWhiteSpace(appName)) return false;
        return System.Text.RegularExpressions.Regex.IsMatch(appName, "^[A-Za-z0-9._-]+$");
    }

    public static bool IsValidServiceName(string serviceName)
    {
        if (string.IsNullOrWhiteSpace(serviceName)) return false;
        return System.Text.RegularExpressions.Regex.IsMatch(serviceName, "^[A-Za-z0-9._-]+$");
    }
}
