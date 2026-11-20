namespace Sandbox.Npcs;

/// <summary>
/// How one NPC regards another entity. Drives target selection -- who to fight, follow,
/// ignore, or flee from.
/// </summary>
public enum Disposition
{
	/// <summary>No strong feeling -- ignore them.</summary>
	Neutral,

	/// <summary>An ally -- protect or assist.</summary>
	Friendly,

	/// <summary>An enemy -- attack on sight.</summary>
	Hostile,

	/// <summary>A threat to avoid -- flee from them.</summary>
	Fearful,
}

/// <summary>
/// A disposition plus a priority. Priority breaks ties when an NPC must choose between
/// several valid targets -- higher wins.
/// </summary>
public readonly record struct Relationship( Disposition Disposition, int Priority = 0 );
