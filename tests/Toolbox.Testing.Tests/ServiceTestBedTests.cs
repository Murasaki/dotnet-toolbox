using Microsoft.Extensions.DependencyInjection;
using Samples.Core.Items;
using Samples.Service;

namespace Toolbox.Testing.Tests;

public class ServiceTestBedTests
{
    private readonly ServiceTestBed _testBed = new(builder => builder.Services.AddSamplesService());

    [Fact]
    public Task Given_Valid_Names_When_Processing_The_Queue_Then_Stores_Them_As_Items() =>
        _testBed
            .WithSetup(services =>
            {
                var queue = services.GetRequiredService<ItemQueue>();
                queue.Enqueue("  Widget  ");
                queue.Enqueue("Gadget");
                return Task.CompletedTask;
            })
            .WithAssertion(async services =>
            {
                var items = await services.GetRequiredService<IItemRepository>().GetAllAsync();
                Assert.Equal(["Gadget", "Widget"], items.Select(i => i.Name).Order());
            })
            .Run(until: async services =>
                (await services.GetRequiredService<IItemRepository>().GetAllAsync()).Count == 2);

    [Fact]
    public Task Given_A_Blank_Name_When_Processing_The_Queue_Then_Skips_It() =>
        _testBed
            .WithSetup(services =>
            {
                var queue = services.GetRequiredService<ItemQueue>();
                queue.Enqueue(" ");
                queue.Enqueue("Widget");
                return Task.CompletedTask;
            })
            .WithAssertion(async services =>
            {
                var items = await services.GetRequiredService<IItemRepository>().GetAllAsync();
                Assert.Equal("Widget", Assert.Single(items).Name);
            })
            .Run(until: async services =>
                (await services.GetRequiredService<IItemRepository>().GetAllAsync()).Count >= 1);

    [Fact]
    public Task Given_A_Condition_That_Is_Never_Met_When_Running_Then_Throws_Timeout() =>
        Assert.ThrowsAsync<TimeoutException>(() =>
            _testBed.Run(until: _ => Task.FromResult(false), timeout: TimeSpan.FromMilliseconds(200)));
}
