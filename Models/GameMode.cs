namespace GeoQuest.Models;

/// <summary>
/// How a run is played. The mode is chosen per run rather than configured, because it
/// changes what the run is rather than how it is presented.
/// </summary>
public enum GameMode
{
    /// <summary>The classic run: uniform questions, lives from Options, extra lives possible.</summary>
    Normal,

    /// <summary>A fixed-length session at a steady difficulty, with nothing to lose.</summary>
    Learning,

    /// <summary>Three lives, no way to earn more, and a grid that opens wider.</summary>
    Hard,
}
