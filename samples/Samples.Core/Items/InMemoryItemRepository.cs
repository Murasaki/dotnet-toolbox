using System.Collections.Concurrent;

namespace Samples.Core.Items;

public sealed class InMemoryItemRepository : IItemRepository
{
    private readonly ConcurrentDictionary<Guid, Item> _items = new();

    public Task<IReadOnlyList<Item>> GetAllAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Item>>(_items.Values.OrderBy(i => i.CreatedAt).ToList());

    public Task<Item?> GetAsync(Guid id, CancellationToken ct = default) =>
        Task.FromResult(_items.GetValueOrDefault(id));

    public Task AddAsync(Item item, CancellationToken ct = default)
    {
        if (!_items.TryAdd(item.Id, item))
            throw new InvalidOperationException($"Item {item.Id} already exists.");

        return Task.CompletedTask;
    }
}
