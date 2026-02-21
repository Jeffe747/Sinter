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
                
                // Find DLL in the NEW publish dir
                var dllFiles = Directory.GetFiles(publishDir, "*.dll");
                var dllName = $"{appName}.dll"; // Default
                if (!File.Exists(Path.Combine(publishDir, dllName)))
                {
                     var likelyDll = dllFiles.FirstOrDefault(f => !f.EndsWith("mscrolib.dll"));
                     if (likelyDll != null) dllName = Path.GetFileName(likelyDll);
                }
                
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
}
