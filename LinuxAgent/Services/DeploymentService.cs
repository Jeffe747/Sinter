using System.Text;
using System.Threading.Channels;

namespace LinuxAgent.Services;

public class DeploymentService
{
    private readonly ICommandRunner _runner;
    private readonly ILogger<DeploymentService> _logger;

    public DeploymentService(ICommandRunner runner, ILogger<DeploymentService> logger)
    {
        _runner = runner;
        _logger = logger;
    }

    public async IAsyncEnumerable<string> DeployAsync(string repoUrl, string appName, string? branch = "main", string? token = null, bool dryRun = false, string? projectPath = null)
    {
        var channel = Channel.CreateUnbounded<string>();
        
        _ = Task.Run(async () => 
        {
            var baseDir = $"/opt/linux-agent/apps/{appName}";
            var repoDir = Path.Combine(baseDir, "repo");
            var releasesDir = Path.Combine(baseDir, "releases");
            var currentLink = Path.Combine(baseDir, "current");

            var timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
            var newReleaseDir = Path.Combine(releasesDir, timestamp);

            // Helper to log via channel
            async Task Log(string message) => await channel.Writer.WriteAsync(message);

            try
            {
                // 1. Prepare Directories
                if (!dryRun)
                {
                     Directory.CreateDirectory(repoDir);
                     Directory.CreateDirectory(releasesDir);
                }
                else
                {
                     // For Dry Run, use temp
                     repoDir = Path.Combine(Path.GetTempPath(), $"linux-agent-dryrun-repo-{Guid.NewGuid()}");
                     newReleaseDir = Path.Combine(Path.GetTempPath(), $"linux-agent-dryrun-release-{Guid.NewGuid()}");
                     Directory.CreateDirectory(repoDir);
                     Directory.CreateDirectory(newReleaseDir);
                     await Log($"[INFO] DRY RUN: Using temp directories.");
                }

                // 2. Clone or Fetch
                // Inject token into URL if provided
                var safeRepoUrl = repoUrl;
                if (string.IsNullOrEmpty(token))
                {
                    token = Environment.GetEnvironmentVariable("GITHUB_TOKEN");
                }

                if (!string.IsNullOrEmpty(token))
                {
                    if (repoUrl.StartsWith("https://"))
                    {
                        safeRepoUrl = repoUrl.Insert(8, $"oauth2:{token}@");
                        await Log($"[INFO] Using provided token for authentication.");
                    }
                }

                if (Directory.Exists(Path.Combine(repoDir, ".git")))
                {
                     await Log($"[INFO] Fetching latest changes in {repoDir}...");
                     // Reset and pull to ensure clean state
                     await foreach (var log in _runner.StreamAsync("git", "fetch origin", repoDir)) await Log(log);
                     await foreach (var log in _runner.StreamAsync("git", $"reset --hard origin/{branch}", repoDir)) await Log(log);
                }
                else
                {
                     await Log($"[INFO] Cloning repository to {repoDir}...");
                     await foreach (var log in _runner.StreamAsync("git", $"clone -b {branch} {safeRepoUrl} .", repoDir)) await Log(log);
                }

                // 3. Publish to New Release Directory
                await Log($"[INFO] Publishing to {newReleaseDir}...");
                var publishDir = Path.Combine(newReleaseDir, "publish");
                
                bool publishSuccess = true;
                var publishArgs = $"publish -c Release -o {publishDir}";
                if (!string.IsNullOrEmpty(projectPath))
                {
                     publishArgs = $"publish {projectPath} -c Release -o {publishDir}";
                     await Log($"[INFO] Building specific project: {projectPath}");
                }

                await foreach (var log in _runner.StreamAsync("dotnet", publishArgs, repoDir))
                {
                    await Log(log);
                    if (log.Contains("Build FAILED")) publishSuccess = false;
                }

                if (!publishSuccess)
                {
                     await Log($"[FAIL] Build/Publish failed.");
                     return;
                }

                if (dryRun)
                {
                    await Log($"[SUCCESS] Dry Run completed. Build verified.");
                    return;
                }

                // 4. Update Symlink (Atomic Switch)
                // Prepare the potential rollback target (current active release)
                string? previousReleasePath = null;
                if (Directory.Exists(currentLink) || File.Exists(currentLink))
                {
                     try {
                        previousReleasePath = new FileInfo(currentLink).ResolveLinkTarget(true)?.FullName;
                     } catch {}
                }
                
                await Log($"[INFO] Updating 'current' symlink to {publishDir}...");
                // Create symbolic link: ln -sfn target link_name
                await _runner.RunAsync("ln", $"-sfn {publishDir} {currentLink}");

                // 5. Create/Update Systemd Service
                var serviceName = $"{appName}.service";
                var servicePath = $"/etc/systemd/system/{serviceName}";
                
                // Find entry DLL in the NEW publish dir
                var dllFiles = Directory.GetFiles(publishDir, "*.dll");
                var dllCandidates = new List<string>();

                // 1) If projectPath was provided, prefer its project name as entry assembly
                if (!string.IsNullOrWhiteSpace(projectPath))
                {
                    var projectName = Path.GetFileNameWithoutExtension(projectPath);
                    if (!string.IsNullOrWhiteSpace(projectName))
                    {
                        dllCandidates.Add($"{projectName}.dll");
                    }
                }

                // 2) Prefer assembly inferred from *.runtimeconfig.json (publish output entrypoint)
                var runtimeConfig = Directory.GetFiles(publishDir, "*.runtimeconfig.json")
                    .Select(Path.GetFileName)
                    .FirstOrDefault();

                if (!string.IsNullOrWhiteSpace(runtimeConfig))
                {
                    var runtimeConfigBase = runtimeConfig.Replace(".runtimeconfig.json", string.Empty);
                    if (!string.IsNullOrWhiteSpace(runtimeConfigBase))
                    {
                        dllCandidates.Add($"{runtimeConfigBase}.dll");
                    }
                }

                // 3) Keep appName-based convention as fallback
                dllCandidates.Add($"{appName}.dll");

                string? dllName = null;
                foreach (var candidate in dllCandidates.Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    if (File.Exists(Path.Combine(publishDir, candidate)))
                    {
                        dllName = candidate;
                        break;
                    }
                }

                // 4) Final fallback: choose a non-framework DLL with matching deps file if possible
                if (string.IsNullOrWhiteSpace(dllName))
                {
                    var depsBased = dllFiles
                        .Select(Path.GetFileName)
                        .Where(name => !string.IsNullOrWhiteSpace(name))
                        .FirstOrDefault(name =>
                            !name!.StartsWith("Microsoft.", StringComparison.OrdinalIgnoreCase) &&
                            !name.StartsWith("System.", StringComparison.OrdinalIgnoreCase) &&
                            File.Exists(Path.Combine(publishDir, Path.GetFileNameWithoutExtension(name) + ".deps.json")));

                    if (!string.IsNullOrWhiteSpace(depsBased))
                    {
                        dllName = depsBased;
                    }
                }

                if (string.IsNullOrWhiteSpace(dllName))
                {
                    await Log("[FAIL] Could not determine application entry DLL in publish output.");
                    return;
                }

                await Log($"[INFO] Selected entry DLL: {dllName}");
                
                // ExecStart uses the 'current' symlink path
                var dllLinkPath = Path.Combine(currentLink, dllName);

                var serviceContent = $@"
[Unit]
Description={appName} - Managed by LinuxAgent
After=network.target

[Service]
WorkingDirectory={currentLink}
ExecStart=/usr/local/bin/dotnet {dllLinkPath}
Restart=always
RestartSec=10
SyslogIdentifier={appName}
User=root
Environment=ASPNETCORE_ENVIRONMENT=Production
Environment=DOTNET_ROOT=/usr/local/share
Environment=DataDirectory=/opt/linux-agent/apps/{appName}/data

[Install]
WantedBy=multi-user.target
";
                
                await Log($"[INFO] Writing service file to {servicePath}");
                await File.WriteAllTextAsync(servicePath, serviceContent);
        
                // 6. Reload and Restart
                await Log($"[INFO] Restarting service {serviceName}...");
                await _runner.RunAsync("systemctl", "daemon-reload");
                await _runner.RunAsync("systemctl", $"enable {serviceName}");
                var restartResult = await _runner.RunAsync("systemctl", $"restart {serviceName}");

                if (restartResult.ExitCode != 0)
                {
                    await Log($"[FAIL] Service failed to start. Initiating Rollback...");
                    
                    if (!string.IsNullOrEmpty(previousReleasePath) && Directory.Exists(previousReleasePath))
                    {
                         await Log($"[INFO] Rolling back to {previousReleasePath}...");
                         await _runner.RunAsync("ln", $"-sfn {previousReleasePath} {currentLink}");
                         await _runner.RunAsync("systemctl", $"restart {serviceName}");
                         await Log($"[INFO] Rollback successful.");
                    }
                    else
                    {
                        await Log($"[ERR] No previous version found to rollback to.");
                    }
                }
                else
                {
                    await Log($"[SUCCESS] Deployment of {appName} completed successfully.");
                    
                    // 6.5 Save deployment metadata for redeploying
                    try
                    {
                        var deployJson = System.Text.Json.JsonSerializer.Serialize(new
                        {
                            repoUrl,
                            appName,
                            branch,
                            token,
                            projectPath
                        }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                        await File.WriteAllTextAsync(Path.Combine(baseDir, "deploy.json"), deployJson);
                    }
                    catch (Exception ex)
                    {
                        await Log($"[WARN] Could not save deploy.json metadata: {ex.Message}");
                    }

                    // 7. Cleanup Old Builds
                    try 
                    {
                        var releases = Directory.GetDirectories(releasesDir).OrderByDescending(d => d).ToList();
                        if (releases.Count > 5)
                        {
                            var toDelete = releases.Skip(5);
                            foreach (var oldRelease in toDelete)
                            {
                                // Safety check: Don't delete the current one or the one we just deployed
                                 if (Path.GetFullPath(oldRelease) == Path.GetFullPath(newReleaseDir)) continue;
                                 
                                 await Log($"[INFO] Cleaning up old release: {Path.GetFileName(oldRelease)}");
                                 Directory.Delete(oldRelease, true);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        await Log($"[WARN] Cleanup failed: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                 await Log($"[ERR] Unexpected error during deployment: {ex.Message}");
            }
            finally
            {
                if (dryRun && Directory.Exists(repoDir) && repoDir.Contains("dryrun"))
                {
                    try { Directory.Delete(repoDir, true); Directory.Delete(newReleaseDir, true); } catch { }
                }
                channel.Writer.TryComplete();
            }
        });

        await foreach (var line in channel.Reader.ReadAllAsync())
        {
            yield return line;
        }
    }

    public async IAsyncEnumerable<string> RemoveDeploymentAsync(string appName)
    {
        var channel = Channel.CreateUnbounded<string>();

        _ = Task.Run(async () =>
        {
            var baseDir = $"/opt/linux-agent/apps/{appName}";
            var serviceName = $"{appName}.service";
            var servicePath = $"/etc/systemd/system/{serviceName}";
            var serviceDropInDir = $"/etc/systemd/system/{serviceName}.d";

            async Task Log(string message) => await channel.Writer.WriteAsync(message);

            async Task<bool> RunAndLog(string command, string args, bool failOnError = true)
            {
                var result = await _runner.RunAsync(command, args);

                if (!string.IsNullOrWhiteSpace(result.StdOut))
                {
                    foreach (var line in result.StdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    {
                        await Log($"[INFO] {line}");
                    }
                }

                if (!string.IsNullOrWhiteSpace(result.StdErr))
                {
                    foreach (var line in result.StdErr.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    {
                        await Log($"[ERR]  {line}");
                    }
                }

                if (result.ExitCode != 0)
                {
                    if (failOnError)
                    {
                        await Log($"[FAIL] Command failed: {command} {args}");
                        return false;
                    }

                    await Log($"[WARN] Command returned non-zero (ignored): {command} {args}");
                }

                return true;
            }

            try
            {
                var appExists = Directory.Exists(baseDir);
                var serviceExists = File.Exists(servicePath);

                if (!appExists && !serviceExists)
                {
                    await Log($"[FAIL] Nothing to delete. App directory and service file not found for '{appName}'.");
                    return;
                }

                await Log($"[INFO] Starting removal for '{appName}'.");

                if (serviceExists)
                {
                    await Log($"[INFO] Stopping service {serviceName}...");
                    await RunAndLog("systemctl", $"stop {serviceName}", failOnError: false);

                    await Log($"[INFO] Disabling service {serviceName}...");
                    await RunAndLog("systemctl", $"disable {serviceName}", failOnError: false);

                    await Log($"[INFO] Removing service file {servicePath}...");
                    File.Delete(servicePath);

                    if (Directory.Exists(serviceDropInDir))
                    {
                        await Log($"[INFO] Removing service drop-in directory {serviceDropInDir}...");
                        Directory.Delete(serviceDropInDir, true);
                    }

                    await Log("[INFO] Reloading systemd daemon...");
                    if (!await RunAndLog("systemctl", "daemon-reload")) return;

                    await Log($"[INFO] Resetting failed state for {serviceName}...");
                    await RunAndLog("systemctl", $"reset-failed {serviceName}", failOnError: false);
                }
                else
                {
                    await Log($"[WARN] Service file not found for {serviceName}; skipping service cleanup.");
                }

                if (appExists)
                {
                    await Log($"[INFO] Deleting app directory {baseDir}...");
                    Directory.Delete(baseDir, true);
                }
                else
                {
                    await Log($"[WARN] App directory not found at {baseDir}; skipping file cleanup.");
                }

                await Log($"[SUCCESS] Removal of '{appName}' completed.");
            }
            catch (Exception ex)
            {
                await Log($"[ERR] Unexpected error during removal: {ex.Message}");
            }
            finally
            {
                channel.Writer.TryComplete();
            }
        });

        await foreach (var line in channel.Reader.ReadAllAsync())
        {
            yield return line;
        }
    }
}
