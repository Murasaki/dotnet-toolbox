using Microsoft.Extensions.DependencyInjection;
using Samples.Core.Items;

namespace Samples.Cmdline;

public static class ServiceCollectionExtensions
{
    // Shared by Program and the tests so both run the same registrations.
    public static IServiceCollection AddSamplesCmdline(this IServiceCollection services) =>
        services
            .AddSingleton(TimeProvider.System)
            .AddSingleton<IItemRepository, InMemoryItemRepository>()
            .AddTransient<CommandRunner>();
}
