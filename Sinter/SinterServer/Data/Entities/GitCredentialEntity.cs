namespace SinterServer.Data.Entities;

public sealed class GitCredentialEntity
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Username { get; set; }
    public string EncryptedAccessToken { get; set; } = string.Empty;
    public ICollection<ApplicationEntity> Applications { get; set; } = [];
}