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
	/// Uniform scale multiplier applied on top of whatever scale the scatterer picks per instance.
	/// </summary>
	[Property, Range( 0.1f, 5f )]
	public float LocalScale { get; set; } = 1.0f;

	/// <summary>
	/// Whether instances of this entry cast shadows.
	/// </summary>
	[Property]
	public bool CastShadows { get; set; } = true;

	/// <summary>
	/// Whether instances of this entry get a physics collision body. Only affects model-based
	/// entries - prefab entries bring their own physics via their own components.
	/// </summary>
	[Property]
	public bool EnablePhysics { get; set; } = true;

	/// <summary>
	/// Returns whether this entry has a valid asset to spawn.
	/// </summary>
	public bool HasAsset => Prefab is not null || Model is not null;

	/// <summary>
	/// Returns the primary asset reference as a string for debugging.
	/// </summary>
	public string AssetName => Prefab?.Name ?? Model?.Name ?? "None";
}
