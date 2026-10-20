using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Samples.Core.Items;

namespace Samples.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class ItemsController(IItemRepository repository, TimeProvider clock) : ControllerBase
{
    [HttpGet]
    public async Task<IReadOnlyList<Item>> GetAll(CancellationToken ct) =>
        await repository.GetAllAsync(ct);

    [HttpGet("{id:guid}")]
    [ProducesResponseType<Item>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<Item>> Get(Guid id, CancellationToken ct)
    {
        var item = await repository.GetAsync(id, ct);
        return item is null ? NotFound() : item;
    }

    [HttpPost]
    [ProducesResponseType<Item>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<Item>> Create(CreateItemRequest request, CancellationToken ct)
    {
        var item = new Item(Guid.NewGuid(), request.Name.Trim(), clock.GetUtcNow());
        await repository.AddAsync(item, ct);
        return CreatedAtAction(nameof(Get), new { id = item.Id }, item);
    }
}

public sealed record CreateItemRequest([Required, StringLength(100, MinimumLength = 1)] string Name);
