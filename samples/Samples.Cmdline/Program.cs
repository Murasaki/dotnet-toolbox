using Microsoft.Extensions.DependencyInjection;
using Samples.Cmdline;

await using var services = new ServiceCollection().AddSamplesCmdline().BuildServiceProvider();

return await services.GetRequiredService<CommandRunner>().RunAsync(args, Console.Out, Console.Error);
