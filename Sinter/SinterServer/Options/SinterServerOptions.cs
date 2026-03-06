namespace SinterServer.Options;

public sealed class SinterServerOptions
{
    public const string SectionName = "SinterServer";

    public string DatabasePath { get; set; } = "./data/sinter-server.db";
    public int PollIntervalSeconds { get; set; } = 30;
    public string ServerName { get; set; } = "SinterServer";
}