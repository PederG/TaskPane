using TaskPane.Core.Ids;

namespace TaskPane.Core.Entities;

public sealed class Tag
{
    public required TagId Id { get; init; }
    public required string Name { get; set; }
    public string? Color { get; set; }
    public DateTimeOffset CreatedUtc { get; set; }
}
