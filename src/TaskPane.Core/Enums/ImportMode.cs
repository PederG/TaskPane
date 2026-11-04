namespace TaskPane.Core.Enums;

/// <summary>
/// Phase 6 import collision policy.
/// MergeById keeps stable IDs; ImportAsNew remaps every entity ID.
/// </summary>
public enum ImportMode
{
    /// <summary>Keep IDs; skip entities whose IDs already exist.</summary>
    MergeByIdSkipConflicts = 0,

    /// <summary>Keep IDs; fail the whole import if any ID already exists.</summary>
    MergeByIdFailOnConflict = 1,

    /// <summary>Assign fresh IDs for every imported entity and remap references.</summary>
    ImportAsNew = 2
}
