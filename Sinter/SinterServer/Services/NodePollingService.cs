using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SinterServer.Data;
using SinterServer.Options;

namespace SinterServer.Services;

public sealed class NodePollingService(IServiceScopeFactory scopeFactory, IOptions<SinterServerOptions> options) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<SinterServerDbContext>();
                var registryService = scope.ServiceProvider.GetRequiredService<IRegistryService>();
                var nodeIds = await dbContext.Nodes.AsNoTracking().Select(node => node.Id).ToArrayAsync(stoppingToken);
                foreach (var nodeId in nodeIds)
                {
                    try
                    {
                        await registryService.RefreshNodeAsync(nodeId, stoppingToken);
                    }
                    catch
                    {
                    }
                }
            }
            catch
            {
            }

            await Task.Delay(TimeSpan.FromSeconds(Math.Max(10, options.Value.PollIntervalSeconds)), stoppingToken);
        }
    }
}