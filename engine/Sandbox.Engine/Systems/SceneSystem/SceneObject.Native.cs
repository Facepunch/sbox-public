namespace Sandbox;

public partial class SceneObject
{
	/// <summary>From made until deleted or destroyed.</summary>
	bool _alive;

	/// <summary>
	/// Whether it has a native object: only one native code made (a native tool's preview objects, Hammer's lights, probes and
	/// helpers) or a buffer object. Scene objects managed code makes have none.
	/// </summary>
	internal bool HasNativeObject => !native.IsNull;

	private protected Model _keptModel;
	bool _batchable = true;

	/// <summary>
	/// Whether a change also goes to the native object: only for one native code made, whose native code reads it back. A
	/// deleted object throws, as a call on its gone native object did.
	/// </summary>
	private protected bool WritesNativeObject( string method )
	{
		if ( !_alive ) Sandbox.Interop.ThrowNullSelf( "CSceneObject", method );
		return !native.IsNull;
	}

	/// <summary>
	/// Made by managed code: in its world and its renderer as <see cref="OnNativeInit"/> puts one native code made, with the
	/// defaults native's new objects had (visible, white, no layer match, no LOD override, batchable, the model's default mesh
	/// groups or all of them without a model).
	/// </summary>
	private protected void MakeManaged( SceneWorld world, Model model, Rendering.SceneObjectFlags flags = default, bool batchable = true )
	{
		_alive = true;
		World = world;
		_flags ??= flags;
		_batchable = batchable;
		_transform = Transform.Zero; // where native's new object was until it was placed
									 // Native's mesh instances started on the model's default groups; an object without a model reports every group
		_meshGroupMask = model is { native.IsNull: false } ? model.native.GetDefaultMeshGroupMask() : ulong.MaxValue;

		lock ( World.InternalSceneObjects )
		{
			World.InternalSceneObjects.Add( this );
			World.InvalidateSceneObjects();
		}

		World.Render?.Made( this );
	}
}
