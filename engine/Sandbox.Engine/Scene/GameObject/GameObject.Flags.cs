namespace Sandbox;

[Flags]
public enum GameObjectFlags
{
	None = 0,

	/// <summary>
	/// Hide this object in hierarchy/inspector
	/// </summary>
	Hidden = 1,

	/// <summary>
	/// Don't save this object to disk, or when duplicating
	/// </summary>
	NotSaved = 2,

	/// <summary>
	/// Auto created - it's a bone, driven by animation
	/// </summary>
	Bone = 4,

	/// <summary>
	/// Auto created - it's an attachment
	/// </summary>
	Attachment = 8,

	/// <summary>
	/// There's something wrong with this
	/// </summary>
	Error = 16,

	/// <summary>
	/// Loading something
	/// </summary>
	Loading = 32,

	/// <summary>
	/// Is in the process of deserializing
	/// </summary>
	Deserializing = 64,

	/// <summary>
	/// When loading a new scene, keep this gameobject active
	/// </summary>
	DontDestroyOnLoad = 128,

	/// <summary>
	/// Keep local - don't network this object as part of the scene snapshot
	/// </summary>
	NotNetworked = 256,

	/// <summary>
	/// In the process of refreshing from the network
	/// </summary>
	[Obsolete]
	Refreshing = 512,

	/// <summary>
	/// Stops animation stomping the bone, will use the bone's local position
	/// </summary>
	ProceduralBone = 1024,

	/// <summary>
	/// Only exists in the editor. Don't spawn it in game.
	/// </summary>
	EditorOnly = 2048,

	/// <summary>
	/// Ignore the parent transform. Basically, position: absolute for gameobjects.
	/// </summary>
	Absolute = 4096,

	/// <summary>
	/// The position of this object is controlled by by physics - usually via a RigidBody component
	/// </summary>
	PhysicsBone = 8192,

	/// <summary>
	/// Stops this object being interpolated, either via the network system or the physics system
	/// </summary>
	NoInterpolation = 16384,
}


public partial class GameObject
{
	public GameObjectFlags Flags { get; set; } = GameObjectFlags.None;

	/// <summary>
	/// True if this GameObject is being deserialized right now
	/// </summary>
	public bool IsDeserializing => Flags.Contains( GameObjectFlags.Deserializing );

	/// <summary>
	/// Do we or our ancestor have this flag
	/// </summary>
	internal bool HasFlagOrParent( GameObjectFlags f )
	{
		if ( Flags.Contains( f ) ) return true;
		if ( Parent is null ) return false;

		return Parent.HasFlagOrParent( f );
	}
}
