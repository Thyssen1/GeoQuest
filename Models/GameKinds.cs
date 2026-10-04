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

    /// <summary>Find the City: a capital is named, and placed by dropping a pin on the world.</summary>
    Cities,
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

/// <summary>
/// How the player answers: by pointing at a tile, by naming the country, or by dropping a
/// pin on the map. The first two pick from what is offered; the third is the only one with
/// no wrong answer to choose, only a distance to be judged on.
/// </summary>
public enum RoundInput
{
    Grid,

    Name,

    Pin,
}

/// <summary>
/// What a round draws to stand for a country. Knowing a flag and knowing an outline are
/// different things, which is why each game keeps its own progress.
/// </summary>
public enum RoundSubject
{
    Flag,

    Outline,

    /// <summary>A place on the world map, named rather than drawn.</summary>
    Place,
}
