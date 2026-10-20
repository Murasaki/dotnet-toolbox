namespace Samples.Core.Items;

public interface IItemRepository
{
    Task<IReadOnlyList<Item>> GetAllAsync(CancellationToken ct = default);
    Task<Item?> GetAsync(Guid id, CancellationToken ct = default);
    Task AddAsync(Item item, CancellationToken ct = default);
}
