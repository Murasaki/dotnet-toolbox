using Samples.Core.Items;

namespace Samples.Service;

public static class ServiceCollectionExtensions
{
    // Shared by Program and the tests so both run the same registrations.
    public static IServiceCollection AddSamplesService(this IServiceCollection services) =>
        services
            .AddSingleton(TimeProvider.System)
            .AddSingleton<IItemRepository, InMemoryItemRepository>()
            .AddSingleton<ItemQueue>()
            .AddHostedService<ItemIngestWorker>();
}
