using Microsoft.EntityFrameworkCore;
using SinterServer.Data.Entities;

namespace SinterServer.Data;

public sealed class SinterServerDbContext(DbContextOptions<SinterServerDbContext> options) : DbContext(options)
{
    public DbSet<NodeEntity> Nodes => Set<NodeEntity>();
    public DbSet<ApplicationEntity> Applications => Set<ApplicationEntity>();
    public DbSet<GitCredentialEntity> GitCredentials => Set<GitCredentialEntity>();
    public DbSet<NodeTelemetrySampleEntity> NodeTelemetrySamples => Set<NodeTelemetrySampleEntity>();
    public DbSet<OperationLogEntity> OperationLogs => Set<OperationLogEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<NodeEntity>().HasIndex(node => node.Name).IsUnique();
        modelBuilder.Entity<NodeEntity>().HasIndex(node => node.Url).IsUnique();
        modelBuilder.Entity<GitCredentialEntity>().HasIndex(credential => credential.Name).IsUnique();
        modelBuilder.Entity<ApplicationEntity>().HasIndex(application => application.Name).IsUnique();

        modelBuilder.Entity<ApplicationEntity>()
            .HasOne(application => application.Node)
            .WithMany(node => node.Applications)
            .HasForeignKey(application => application.NodeId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<ApplicationEntity>()
            .HasOne(application => application.GitCredential)
            .WithMany(credential => credential.Applications)
            .HasForeignKey(application => application.GitCredentialId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<NodeTelemetrySampleEntity>()
            .HasIndex(sample => new { sample.NodeId, sample.CapturedUtc });

        modelBuilder.Entity<NodeTelemetrySampleEntity>()
            .HasOne(sample => sample.Node)
            .WithMany(node => node.TelemetrySamples)
            .HasForeignKey(sample => sample.NodeId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}