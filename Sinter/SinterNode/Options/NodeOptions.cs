namespace SinterNode.Options;

public sealed class NodeOptions
{
    public const string SectionName = "SinterNode";

    public string StateFilePath { get; set; } = "/var/lib/sinter-node/config/node-state.json";
    public string ApiKeyFilePath { get; set; } = "/var/lib/sinter-node/config/client_secret";
    public string ManagedAppsRoot { get; set; } = "/var/lib/sinter-node/apps";
    public string NodeInstallRoot { get; set; } = "/opt/sinter-node";
    public string NodeReleaseRoot { get; set; } = "/var/lib/sinter-node/node";
    public string SystemdUnitDirectory { get; set; } = "/etc/systemd/system";
    public string DotnetPath { get; set; } = "/usr/local/bin/dotnet";
    public string SelfServiceName { get; set; } = "sinter-node.service";
    public string SelfProjectPath { get; set; } = "Sinter/SinterNode/SinterNode.csproj";
    public string SelfUpdateScriptPath { get; set; } = "/opt/sinter-node/current/update.sh";
    public string SelfUpdateLogPath { get; set; } = "/var/log/sinter-node-self-update.log";
    public string DefaultSourceRepository { get; set; } = "https://github.com/Jeffe747/Sinter.git";
    public int RetainedReleaseCount { get; set; } = 5;
}