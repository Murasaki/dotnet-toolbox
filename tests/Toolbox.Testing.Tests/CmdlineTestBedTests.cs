using Microsoft.Extensions.DependencyInjection;
using Samples.Cmdline;
using Samples.Core.Items;

namespace Toolbox.Testing.Tests;

public class CmdlineTestBedTests
{
    private readonly CmdlineTestBed _testBed = new(
        services => services.AddSamplesCmdline(),
        (services, args, output, error) => services.GetRequiredService<CommandRunner>().RunAsync(args, output, error));

    [Fact]
    public Task Given_Existing_Items_When_Listing_Then_Prints_Them()
    {
        var item = new Item(Guid.NewGuid(), "Widget", DateTimeOffset.UtcNow);

        return _testBed
            .WithSetup(services => services.GetRequiredService<IItemRepository>().AddAsync(item))
            .WithAssertion(result =>
            {
                Assert.Equal(0, result.ExitCode);
                Assert.Contains($"{item.Id} Widget", result.Output);
            })
            .Run("list");
    }

    [Fact]
    public Task Given_A_Valid_Name_When_Adding_Then_Stores_It_Trimmed() =>
        _testBed
            .WithAssertion(async (result, services) =>
            {
                Assert.Equal(0, result.ExitCode);

                var id = Guid.Parse(result.Output.Trim());
                var stored = await services.GetRequiredService<IItemRepository>().GetAsync(id);

                Assert.Equal("Widget", stored?.Name);
            })
            .Run("add", "  Widget  ");

    [Theory]
    [InlineData]
    [InlineData("unknown")]
    [InlineData("add")]
    [InlineData("add", " ")]
    public Task Given_Invalid_Arguments_When_Running_Then_Prints_Usage_And_Returns_1(params string[] args) =>
        _testBed
            .WithAssertion(result =>
            {
                Assert.Equal(1, result.ExitCode);
                Assert.StartsWith("Usage:", result.Error);
                Assert.Empty(result.Output);
            })
            .Run(args);
}
