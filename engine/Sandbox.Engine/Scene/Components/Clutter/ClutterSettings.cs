namespace Sandbox.Clutter;

/// <summary>
/// Immutable settings for clutter generation.
/// Used to detect changes and configure the grid system.
/// </summary>
readonly record struct ClutterSettings
{
	public int RandomSeed { get; init; }
	public ClutterDefinition Clutter { get; init; }

	public ClutterSettings( int randomSeed, ClutterDefinition definition )
	{
		RandomSeed = randomSeed;
		Clutter = definition;
	}

	/// <summary>
	/// Validates that settings are ready for clutter generation.
	/// </summary>
	public bool IsValid => Clutter != null;

	public override int GetHashCode()
	{
		return HashCode.Combine(
			Clutter.TileSize,
			Clutter.TileRadius,
			RandomSeed,
			Clutter?.GetHashCode() ?? 0
		);
	}
}
