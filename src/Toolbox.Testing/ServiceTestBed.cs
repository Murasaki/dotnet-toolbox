using System.Runtime.ExceptionServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Toolbox.Testing;

/// <summary>
/// Arrange/act/assert harness that starts a worker host, waits for a condition, then stops it.
/// Each <see cref="Run"/> builds a fresh host, so state doesn't leak between tests.
/// </summary>
/// <param name="configure">The app's own host setup, the same call its Program makes.</param>
public sealed class ServiceTestBed(Action<HostApplicationBuilder> configure)
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(20);

    private readonly List<Action<IServiceCollection>> _serviceOverrides = [];
    private readonly List<Func<IServiceProvider, Task>> _setups = [];
    private readonly List<Func<IServiceProvider, Task>> _assertions = [];

    /// <summary>Replace or add registrations, applied after the app's own.</summary>
    public ServiceTestBed WithServices(Action<IServiceCollection> configure)
    {
        _serviceOverrides.Add(configure);
        return this;
    }

    /// <summary>Seed state before the host starts.</summary>
    public ServiceTestBed WithSetup(Func<IServiceProvider, Task> setup)
    {
        _setups.Add(setup);
        return this;
    }

    public ServiceTestBed WithAssertion(Action<IServiceProvider> assertion) =>
        WithAssertion(services =>
        {
            assertion(services);
            return Task.CompletedTask;
        });

    /// <summary>Assert on the app's state after the host has stopped.</summary>
    public ServiceTestBed WithAssertion(Func<IServiceProvider, Task> assertion)
    {
        _assertions.Add(assertion);
        return this;
    }

    /// <summary>
    /// Runs the host until <paramref name="until"/> returns true, failing if it doesn't within the timeout.
    /// </summary>
    public async Task Run(Func<IServiceProvider, Task<bool>> until, TimeSpan? timeout = null)
    {
        var builder = Host.CreateApplicationBuilder();
        configure(builder);
        foreach (var overrideServices in _serviceOverrides)
            overrideServices(builder.Services);

        using var host = builder.Build();

        using (var setupScope = host.Services.CreateScope())
        {
            foreach (var setup in _setups)
                await setup(setupScope.ServiceProvider);
        }

        await host.StartAsync();
        try
        {
            await WaitUntil(host.Services, until, timeout ?? DefaultTimeout);
        }
        finally
        {
            await host.StopAsync();
        }

        using var assertScope = host.Services.CreateScope();

        // Runs every assertion, even after one fails, so all failures are reported together.
        var failures = new List<Exception>();
        foreach (var assertion in _assertions)
        {
            try
            {
                await assertion(assertScope.ServiceProvider);
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

    private static async Task WaitUntil(IServiceProvider services, Func<IServiceProvider, Task<bool>> until, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        while (true)
        {
            using (var scope = services.CreateScope())
            {
                if (await until(scope.ServiceProvider))
                    return;
            }

            try
            {
                await Task.Delay(PollInterval, cts.Token);
            }
            catch (OperationCanceledException)
            {
                throw new TimeoutException($"Condition was not met within {timeout.TotalSeconds:0.#}s.");
            }
        }
    }
}
