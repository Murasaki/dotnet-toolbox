# dotnet-toolbox
Reusable boiler plate patterns

Copy what you need into a project; nothing here is published as a package.

## Layout

- `src/` – the reusable pieces, one project per area.
- `samples/` – small apps the pieces are exercised against. Not meant to be copied.
- `tests/` – tests that run each piece against the samples, doubling as usage examples.

```
dotnet test
```

## Toolbox.Testing

TestBeds that run the real app in-process, so tests go through DI, validation and serialisation
instead of mocking them. Every TestBed has the same shape:

- `WithServices(Action<IServiceCollection>)` – swap registrations for fakes (applied after the app's own).
- `WithSetup(Func<IServiceProvider, Task>)` – seed state through the app's services.
- `WithAssertion(...)` – assert on the result and/or the app's services. Every assertion runs even if
  an earlier one fails; multiple failures are reported together as an `AggregateException`.
- `Run(...)` – builds a fresh host/container, runs setups, acts, then asserts.

| TestBed | Constructed with | `Run` | Assertions receive |
|---|---|---|---|
| `ApiTestBed<TProgram>` | `WebApplicationFactory<TProgram>` | `client => client.GetAsync(...)` | `HttpResponseMessage` |
| `CmdlineTestBed` | the app's registrations + an entry point | `params string[] args` | `CommandResult(ExitCode, Output, Error)` |
| `ServiceTestBed` | the app's `HostApplicationBuilder` setup | `until` condition (+ timeout) | the host's services, after it stops |

```csharp
_testBed
    .WithSetup(sp => sp.GetRequiredService<IItemRepository>().AddAsync(item))
    .WithAssertion(response => Assert.Equal(HttpStatusCode.OK, response.StatusCode))
    .Run(client => client.GetAsync($"/api/items/{item.Id}"));
```

For the Cmdline and Service TestBeds to share the app's setup, the app should expose its registrations as an
extension method (e.g. `services.AddMyApp()`) that both `Program.cs` and the TestBed call. The Cmdline app's
command handling should take the args and output/error `TextWriter`s rather than using `Console` directly.

Dependencies: Microsoft.AspNetCore.Mvc.Testing, Microsoft.Extensions.Hosting.
