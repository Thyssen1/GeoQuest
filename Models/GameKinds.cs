namespace GeoQuest.Models;

/// <summary>
/// Which game is being played. A mini-game decides what a round asks about; a
/// <see cref="GameMode"/> decides the terms it is played under. The two are independent:
/// every mode exists for every game.
/// </summary>
public enum MiniGame
{
    /// <summary>Guess the Flag: the country is drawn as its flag.</summary>
    Flags,

    /// <summary>Guess the Border: the country is drawn as its outline.</summary>
    Borders,
}

/// <summary>How a run is played. Chosen per run rather than configured.</summary>
public enum GameMode
{
    /// <summary>The classic run: lives from Options, extra lives possible.</summary>
    Normal,

    /// <summary>A fixed-length session at a steady difficulty, with nothing to lose.</summary>
    Learning,

    /// <summary>Three lives and no way to earn more.</summary>
    Hard,

    /// <summary>The question runs the other way: the picture is shown and the country named.</summary>
    Recall,
}

/// <summary>How the player answers: by pointing at a tile, or by naming the country.</summary>
public enum RoundInput
{
    Grid,

    Name,
}

/// <summary>
/// What a round draws to stand for a country. Knowing a flag and knowing an outline are
/// different things, which is why each game keeps its own progress.
/// </summary>
public enum RoundSubject
{
    Flag,

    Outline,
}
