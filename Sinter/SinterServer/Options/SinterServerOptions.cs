namespace SinterServer.Options;

public sealed class SinterServerOptions
{
    public const string SectionName = "SinterServer";

    public string DatabasePath { get; set; } = "./data/sinter-server.db";
    public int PollIntervalSeconds { get; set; } = 30;
    public string ServerName { get; set; } = "SinterServer";
    public int TelemetryRetentionDays { get; set; } = 21;
    public int TelemetrySampleIntervalSeconds { get; set; } = 300;
    public string SelfUpdateScriptPath { get; set; } = "/opt/sinter-server/current/update.sh";
    public string SelfUpdateLogPath { get; set; } = "/var/log/sinter-server-self-update.log";
    public string DefaultSourceRepository { get; set; } = "https://github.com/Jeffe747/Sinter.git";
    public string DefaultSourceBranch { get; set; } = "main";
}