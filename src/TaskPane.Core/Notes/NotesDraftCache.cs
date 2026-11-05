using TaskPane.Core.Ids;

namespace TaskPane.Core.Notes;

/// <summary>
/// In-memory unsaved Markdown/plain-text note drafts for the compact overlay.
/// Drafts survive task switches and list refreshes until Save or Cancel.
/// </summary>
public sealed class NotesDraftCache
{
    private readonly Dictionary<TaskId, string> _drafts = new();

    /// <summary>
    /// Returns a previously stashed draft for <paramref name="id"/>, or seeds from
    /// <paramref name="persistedNotes"/> when none is cached.
    /// </summary>
    public string OpenOrRestore(TaskId id, string? persistedNotes)
    {
        if (_drafts.TryGetValue(id, out var draft))
        {
            return draft;
        }

        return persistedNotes ?? string.Empty;
    }

    public void SetDraft(TaskId id, string draft) => _drafts[id] = draft;

    public bool TryGetDraft(TaskId id, out string draft) => _drafts.TryGetValue(id, out draft!);

    public void Clear(TaskId id) => _drafts.Remove(id);

    public bool IsDirty(TaskId id, string? persistedNotes)
    {
        if (!_drafts.TryGetValue(id, out var draft))
        {
            return false;
        }

        return !string.Equals(Normalize(draft), Normalize(persistedNotes), StringComparison.Ordinal);
    }

    /// <summary>Same persist rule as <c>TaskService.UpdateAsync</c>: trim; blank becomes null.</summary>
    public static string? NormalizeForPersist(string? notes)
        => string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();

    private static string Normalize(string? notes) => NormalizeForPersist(notes) ?? string.Empty;
}
