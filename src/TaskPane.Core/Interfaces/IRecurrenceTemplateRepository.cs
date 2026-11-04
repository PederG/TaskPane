using TaskPane.Core.Entities;
using TaskPane.Core.Ids;

namespace TaskPane.Core.Interfaces;

public interface IRecurrenceTemplateRepository
{
    Task<RecurrenceTemplate?> GetByIdAsync(RecurrenceTemplateId id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RecurrenceTemplate>> ListEnabledAsync(CancellationToken cancellationToken = default);

    Task InsertAsync(RecurrenceTemplate template, CancellationToken cancellationToken = default);

    Task UpdateAsync(RecurrenceTemplate template, CancellationToken cancellationToken = default);
}
