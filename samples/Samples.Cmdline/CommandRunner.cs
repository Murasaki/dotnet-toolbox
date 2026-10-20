using Samples.Core.Items;

namespace Samples.Cmdline;

public sealed class CommandRunner(IItemRepository repository, TimeProvider clock)
{
    private const string Usage = "Usage: Samples.Cmdline list | add <name>";

    public async Task<int> RunAsync(string[] args, TextWriter output, TextWriter error, CancellationToken ct = default)
    {
        switch (args)
        {
            case ["list"]:
                foreach (var existing in await repository.GetAllAsync(ct))
                    await output.WriteLineAsync($"{existing.Id} {existing.Name}");
                return 0;

            case ["add", var name] when !string.IsNullOrWhiteSpace(name):
                var item = new Item(Guid.NewGuid(), name.Trim(), clock.GetUtcNow());
                await repository.AddAsync(item, ct);
                await output.WriteLineAsync(item.Id.ToString());
                return 0;

            default:
                await error.WriteLineAsync(Usage);
                return 1;
        }
    }
}
