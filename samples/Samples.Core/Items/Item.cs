namespace Samples.Core.Items;

public sealed record Item(Guid Id, string Name, DateTimeOffset CreatedAt);
