namespace Sandbox.Clutter;

/// <summary>
/// Represents a single weighted entry in a <see cref="ClutterDefinition"/>.
/// Contains either a Prefab or Model reference along with spawn parameters.
/// </summary>
public class ClutterEntry
{
	/// <summary>
	/// Prefab to spawn. If set, this takes priority over <see cref="Model"/>.
	/// </summary>
	[Property]
	public GameObject Prefab { get; set; }

	/// <summary>
	/// Model to spawn as a static prop. Only used if <see cref="Prefab"/> is null.
	/// </summary>
	[Property]
	public Model Model { get; set; }

	/// <summary>
	/// Relative weight for random selection. Higher values = more likely to be chosen.
	/// </summary>
	[Property, Range( 0.01f, 1f )]
	public float Weight { get; set; } = 1.0f;

	/// <summary>
	/// Returns whether this entry has a valid asset to spawn.
	/// </summary>
	public bool HasAsset => Prefab is not null || Model is not null;

	/// <summary>
	/// Returns the primary asset reference as a string for debugging.
	/// </summary>
	public string AssetName => Prefab?.Name ?? Model?.Name ?? "None";
}
