using Samples.Core.Items;

namespace Samples.Service;

public sealed class ItemIngestWorker(
    ItemQueue queue,
    IItemRepository repository,
    TimeProvider clock,
    ILogger<ItemIngestWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var name in queue.ReadAllAsync(stoppingToken))
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                logger.LogWarning("Skipped item with an empty name");
                continue;
            }

            var item = new Item(Guid.NewGuid(), name.Trim(), clock.GetUtcNow());
            await repository.AddAsync(item, stoppingToken);
            logger.LogInformation("Stored item {ItemId} ({Name})", item.Id, item.Name);
        }
    }
}
