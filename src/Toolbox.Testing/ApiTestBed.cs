using System.Runtime.ExceptionServices;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Toolbox.Testing;

/// <summary>
/// Arrange/act/assert harness that sends a request through the real API pipeline.
/// Each <see cref="Run"/> builds a fresh host, so singleton state doesn't leak between tests.
/// </summary>
/// <typeparam name="TProgram">The API's entry point, usually <c>Program</c>.</typeparam>
public sealed class ApiTestBed<TProgram>(WebApplicationFactory<TProgram> factory) where TProgram : class
{
    private readonly List<Action<IServiceCollection>> _serviceOverrides = [];
    private readonly List<Func<IServiceProvider, Task>> _setups = [];
    private readonly List<Func<HttpResponseMessage, IServiceProvider, Task>> _assertions = [];

    /// <summary>Replace or add registrations, applied after the app's own.</summary>
    public ApiTestBed<TProgram> WithServices(Action<IServiceCollection> configure)
    {
        _serviceOverrides.Add(configure);
        return this;
    }

    /// <summary>Seed state before the request is sent.</summary>
    public ApiTestBed<TProgram> WithSetup(Func<IServiceProvider, Task> setup)
    {
        _setups.Add(setup);
        return this;
    }

    public ApiTestBed<TProgram> WithAssertion(Action<HttpResponseMessage> assertion) =>
        WithAssertion((response, _) =>
        {
            assertion(response);
            return Task.CompletedTask;
        });

    public ApiTestBed<TProgram> WithAssertion(Func<HttpResponseMessage, Task> assertion) =>
        WithAssertion((response, _) => assertion(response));

    /// <summary>Assert on the response and/or the app's state after the request.</summary>
    public ApiTestBed<TProgram> WithAssertion(Func<HttpResponseMessage, IServiceProvider, Task> assertion)
    {
        _assertions.Add(assertion);
        return this;
    }

    public async Task Run(Func<HttpClient, Task<HttpResponseMessage>> request)
    {
        await using var app = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                foreach (var configure in _serviceOverrides)
                    configure(services);
            }));

        using (var setupScope = app.Services.CreateScope())
        {
            foreach (var setup in _setups)
                await setup(setupScope.ServiceProvider);
        }

        using var client = app.CreateClient();
        using var response = await request(client);
        using var assertScope = app.Services.CreateScope();

        // Runs every assertion, even after one fails, so all failures are reported together.
        var failures = new List<Exception>();
        foreach (var assertion in _assertions)
        {
            try
            {
                await assertion(response, assertScope.ServiceProvider);
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
