using System.Runtime.ExceptionServices;
using Microsoft.Extensions.DependencyInjection;

namespace Toolbox.Testing;

public sealed record CommandResult(int ExitCode, string Output, string Error);

/// <summary>Runs the app's command handling against the given args and writers, returning the exit code.</summary>
public delegate Task<int> CommandEntryPoint(IServiceProvider services, string[] args, TextWriter output, TextWriter error);

/// <summary>
/// Arrange/act/assert harness that runs a command line app in-process with captured output.
/// Each <see cref="Run"/> builds a fresh container, so state doesn't leak between tests.
/// </summary>
/// <param name="register">The app's own registrations, the same call its Program makes.</param>
/// <param name="entryPoint">How to invoke the app once the container is built.</param>
public sealed class CmdlineTestBed(Action<IServiceCollection> register, CommandEntryPoint entryPoint)
{
    private readonly List<Action<IServiceCollection>> _serviceOverrides = [];
    private readonly List<Func<IServiceProvider, Task>> _setups = [];
    private readonly List<Func<CommandResult, IServiceProvider, Task>> _assertions = [];

    /// <summary>Replace or add registrations, applied after the app's own.</summary>
    public CmdlineTestBed WithServices(Action<IServiceCollection> configure)
    {
        _serviceOverrides.Add(configure);
        return this;
    }

    /// <summary>Seed state before the command runs.</summary>
    public CmdlineTestBed WithSetup(Func<IServiceProvider, Task> setup)
    {
        _setups.Add(setup);
        return this;
    }

    public CmdlineTestBed WithAssertion(Action<CommandResult> assertion) =>
        WithAssertion((result, _) =>
        {
            assertion(result);
            return Task.CompletedTask;
        });

    public CmdlineTestBed WithAssertion(Func<CommandResult, Task> assertion) =>
        WithAssertion((result, _) => assertion(result));

    /// <summary>Assert on the exit code/output and/or the app's state after the command.</summary>
    public CmdlineTestBed WithAssertion(Func<CommandResult, IServiceProvider, Task> assertion)
    {
        _assertions.Add(assertion);
        return this;
    }

    public async Task Run(params string[] args)
    {
        var services = new ServiceCollection();
        register(services);
        foreach (var configure in _serviceOverrides)
            configure(services);

        await using var provider = services.BuildServiceProvider();

        using (var setupScope = provider.CreateScope())
        {
            foreach (var setup in _setups)
                await setup(setupScope.ServiceProvider);
        }

        var output = new StringWriter();
        var error = new StringWriter();
        var exitCode = await entryPoint(provider, args, output, error);
        var result = new CommandResult(exitCode, output.ToString(), error.ToString());

        using var assertScope = provider.CreateScope();

        // Runs every assertion, even after one fails, so all failures are reported together.
        var failures = new List<Exception>();
        foreach (var assertion in _assertions)
        {
            try
            {
                await assertion(result, assertScope.ServiceProvider);
            }
            catch (Exception ex)
            {
                failures.Add(ex);
            }
        }

        ThrowFailures(failures);
    }

    private static void ThrowFailures(List<Exception> failures)
    {
        if (failures.Count == 1)
            ExceptionDispatchInfo.Throw(failures[0]); // keeps the original stack trace

        if (failures.Count > 1)
            throw new AggregateException("Multiple assertions failed.", failures);
    }
}
