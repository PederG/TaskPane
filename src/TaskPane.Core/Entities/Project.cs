using TaskPane.Core.Ids;

namespace TaskPane.Core.Entities;

public sealed class Project
{
    public required ProjectId Id { get; init; }
    public required string Name { get; set; }
    public string? Color { get; set; }
    public DateTimeOffset? ArchivedUtc { get; set; }
    public DateTimeOffset CreatedUtc { get; set; }
    public DateTimeOffset UpdatedUtc { get; set; }

    public bool IsArchived => ArchivedUtc is not null;
}
