namespace TaskPane.Core.Enums;

public enum IdleResolutionChoice
{
    /// <summary>End the session at idle onset; do not count idle minutes.</summary>
    DiscardIdle = 0,

    /// <summary>Keep time through idle onset already recorded; resume tracking after prompt.</summary>
    KeepAndResume = 1,

    /// <summary>Keep time through idle onset already recorded; do not resume.</summary>
    KeepStopped = 2
}
