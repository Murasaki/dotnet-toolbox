using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Samples.Core.Items;

namespace Toolbox.Testing.Tests;

public class ApiTestBedTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly ApiTestBed<Program> _testBed = new(factory);

    [Fact]
    public Task Given_An_Unknown_Item_When_Getting_It_Then_Returns_404() =>
        _testBed
            .WithAssertion(response => Assert.Equal(HttpStatusCode.NotFound, response.StatusCode))
            .Run(client => client.GetAsync($"/api/items/{Guid.NewGuid()}"));

    [Fact]
    public Task Given_An_Existing_Item_When_Getting_It_Then_Returns_It()
    {
        var item = new Item(Guid.NewGuid(), "Widget", DateTimeOffset.UtcNow);

        return _testBed
            .WithSetup(services => services.GetRequiredService<IItemRepository>().AddAsync(item))
            .WithAssertion(async response =>
            {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                Assert.Equal(item, await response.Content.ReadFromJsonAsync<Item>());
            })
            .Run(client => client.GetAsync($"/api/items/{item.Id}"));
    }

    [Fact]
    public Task Given_A_Valid_Name_When_Posting_An_Item_Then_Stores_It_Trimmed() =>
        _testBed
            .WithAssertion(async (response, services) =>
            {
                Assert.Equal(HttpStatusCode.Created, response.StatusCode);

                var created = await response.Content.ReadFromJsonAsync<Item>();
                var stored = await services.GetRequiredService<IItemRepository>().GetAsync(created!.Id);

                Assert.Equal(created, stored);
                Assert.Equal("Widget", created.Name);
            })
            .Run(client => client.PostAsJsonAsync("/api/items", new { name = "  Widget  " }));

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public Task Given_An_Invalid_Name_When_Posting_An_Item_Then_Returns_400(string? name) =>
        _testBed
            .WithAssertion(response => Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode))
            .Run(client => client.PostAsJsonAsync("/api/items", new { name }));

    [Fact]
    public Task Given_A_Service_Override_When_Running_Then_The_App_Uses_It()
    {
        var item = new Item(Guid.NewGuid(), "Seeded", DateTimeOffset.UtcNow);
        var repository = new InMemoryItemRepository();

        return _testBed
            .WithServices(services => services.AddSingleton<IItemRepository>(repository))
            .WithSetup(_ => repository.AddAsync(item))
            .WithAssertion(response => Assert.Equal(HttpStatusCode.OK, response.StatusCode))
            .Run(client => client.GetAsync($"/api/items/{item.Id}"));
    }

    [Fact]
    public async Task Given_Multiple_Failing_Assertions_When_Running_Then_Reports_All_Of_Them()
    {
        var run = () => _testBed
            .WithAssertion(_ => Assert.Fail("first"))
            .WithAssertion(_ => Assert.Fail("second"))
            .Run(client => client.GetAsync("/health"));

        var ex = await Assert.ThrowsAsync<AggregateException>(run);

        Assert.Equal(2, ex.InnerExceptions.Count);
    }
}
