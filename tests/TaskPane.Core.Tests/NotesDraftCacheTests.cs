using TaskPane.Core.Ids;
using TaskPane.Core.Notes;

namespace TaskPane.Core.Tests;

public sealed class NotesDraftCacheTests
{
    [Fact]
    public void Create_draft_seeds_empty_and_marks_dirty_after_edit()
    {
        var cache = new NotesDraftCache();
        var id = TaskId.New();

        var opened = cache.OpenOrRestore(id, persistedNotes: null);
        Assert.Equal(string.Empty, opened);
        Assert.False(cache.IsDirty(id, null));

        cache.SetDraft(id, "First line\n**bold**");
        Assert.True(cache.IsDirty(id, null));
        Assert.Equal("First line\n**bold**", cache.OpenOrRestore(id, null));
    }

    [Fact]
    public void Edit_existing_notes_is_dirty_until_matching_persist()
    {
        var cache = new NotesDraftCache();
        var id = TaskId.New();
        const string persisted = "original note";

        cache.SetDraft(id, cache.OpenOrRestore(id, persisted));
        Assert.False(cache.IsDirty(id, persisted));

        cache.SetDraft(id, "edited note");
        Assert.True(cache.IsDirty(id, persisted));

        // Persist normalization trims — padded draft matching trimmed persist is not dirty.
        cache.SetDraft(id, "  edited note  ");
        Assert.False(cache.IsDirty(id, "edited note"));
        Assert.False(cache.IsDirty(id, "  edited note  "));
    }

    [Fact]
    public void Cancel_clears_draft_and_restores_persisted()
    {
        var cache = new NotesDraftCache();
        var id = TaskId.New();
        const string persisted = "keep me";

        cache.SetDraft(id, "scratch that");
        Assert.True(cache.IsDirty(id, persisted));

        cache.Clear(id);
        Assert.False(cache.IsDirty(id, persisted));
        Assert.Equal(persisted, cache.OpenOrRestore(id, persisted));
    }

    [Fact]
    public void Task_switch_keeps_unsaved_drafts_per_task()
    {
        var cache = new NotesDraftCache();
        var a = TaskId.New();
        var b = TaskId.New();

        cache.SetDraft(a, "draft A");
        cache.SetDraft(b, "draft B");

        Assert.Equal("draft A", cache.OpenOrRestore(a, "persisted A"));
        Assert.Equal("draft B", cache.OpenOrRestore(b, "persisted B"));
        Assert.True(cache.IsDirty(a, "persisted A"));
        Assert.True(cache.IsDirty(b, "persisted B"));

        cache.Clear(b);
        Assert.Equal("persisted B", cache.OpenOrRestore(b, "persisted B"));
        Assert.Equal("draft A", cache.OpenOrRestore(a, "persisted A"));
    }

    [Fact]
    public void NormalizeForPersist_trims_and_nulls_blank()
    {
        Assert.Null(NotesDraftCache.NormalizeForPersist(null));
        Assert.Null(NotesDraftCache.NormalizeForPersist("   "));
        Assert.Equal("line\nline", NotesDraftCache.NormalizeForPersist("  line\nline  "));
    }
}
