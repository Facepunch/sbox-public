namespace Sandbox;

public partial class BaseCombatWeapon
{
	/// <summary>
	/// Which hand(s) a weapon is held in. Matches the animgraph's "holdtype_handedness" options (2H, RH, LH).
	/// </summary>
	public enum WeaponHandedness
	{
		/// <summary>Held in both hands.</summary>
		Both,

		/// <summary>Held in the right hand.</summary>
		Right,

		/// <summary>Held in the left hand.</summary>
		Left
	}

	/// <summary>
	/// Third-person world model prefab, attached to the holder's hand bone. Seen by everyone.
	/// </summary>
	[Property, Feature( "WorldModel" )] public GameObject WorldModelPrefab { get; set; }

	/// <summary>
	/// The bone on the holder's renderer to attach the world model to.
	/// </summary>
	[Property, Feature( "WorldModel" )] public string HoldBone { get; set; } = "hold_r";

	/// <summary>
	/// How the holder poses their arms while this is deployed - an option name on the holder
	/// animgraph's "holdtype" enum parameter (e.g. "pistol", "rifle"). Set on the holder's renderer
	/// when equipped. Empty doesn't drive it.
	/// </summary>
	[Property, Feature( "WorldModel" ), Editor( "holdtype" )] public string HoldType { get; set; } = "";

	/// <summary>
	/// Which hand(s) the holder carries this in - drives the holder animgraph's "holdtype_handedness"
	/// parameter alongside <see cref="HoldType"/>. Only some hold types support it (e.g. pistol,
	/// holditem).
	/// </summary>
	[Property, Feature( "WorldModel" )] public WeaponHandedness Handedness { get; set; } = WeaponHandedness.Both;

	/// <summary>
	/// The spawned world model instance, or null. Not networked - each peer makes its own.
	/// </summary>
	public GameObject WorldModel { get; protected set; }

	/// <summary>
	/// Spawns the world model from <see cref="WorldModelPrefab"/> onto the holder's
	/// <see cref="HoldBone"/>, replacing any previous one.
	/// </summary>
	protected virtual void CreateWorldModel()
	{
		DestroyWorldModel();

		if ( WorldModelPrefab is null )
			return;

		var renderer = HolderRenderer;
		if ( renderer is null )
			return;

		var parent = renderer.GetBoneObject( HoldBone ) ?? GameObject;

		WorldModel = WorldModelPrefab.Clone( new CloneConfig
		{
			Parent = parent,
			StartEnabled = true,
			Transform = global::Transform.Zero
		} );

		WorldModel.Flags |= GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked;
	}

	/// <summary>Destroys the spawned world model, if any.</summary>
	protected virtual void DestroyWorldModel()
	{
		WorldModel?.Destroy();
		WorldModel = null;
	}
}
