using NativeEngine;

namespace Sandbox;

/// <summary>
/// A box which can be used to explicitly control scene visibility. 
/// There are two modes:
/// 1. Cull inside, hide any objects fully inside the box (excluder)
/// 2. Cull outside, hide any objects not intersecting any cull boxes marked cull outside (includer)
/// </summary>
public sealed class SceneCullingBox : IValid
{
	/// <summary>
	/// Cull mode, either inside or outside
	/// </summary>
	public enum CullMode
	{
		/// <summary>
		/// Hide any objects fully inside the box
		/// </summary>
		Inside,

		/// <summary>
		/// Hide any objects not intersecting any boxes
		/// </summary>
		Outside,
	}

	/// <summary>
	/// Is this culling box valid, exists inside a scene world.
	/// </summary>
	public bool IsValid => _boxId != 0 && World.IsValid();

	/// <summary>
	/// The scene world this culling box belongs to.
	/// </summary>
	public SceneWorld World { get; internal set; }

	/// <summary>
	/// Position and rotation of this box, scale will scale the box size
	/// </summary>
	public Transform Transform
	{
		get => _transform;
		set { _transform = value; Update(); }
	}

	/// <summary>
	/// Size of this box, transform scale will scale this size
	/// </summary>
	public Vector3 Size
	{
		get => _size;
		set { _size = value; Update(); }
	}

	/// <summary>
	/// Cull mode, either inside or outside
	/// </summary>
	public CullMode Mode
	{
		get => _mode;
		set { _mode = value; Update(); }
	}

	internal uint _boxId;

	// Whether native's world has the box (SceneObject.WritesNative); otherwise _boxId is one of ours. Borealis doesn't cull
	// by these boxes yet.
	bool inNative;
	static uint nextManagedId;
	internal Transform _transform;
	internal Vector3 _size;
	internal CullMode _mode;

	/// <summary>
	/// Create a scene culling box.
	/// Each scene world can have a list of boxes which can be used to explicitly cull objects inside or outside the boxes.
	/// </summary>
	public SceneCullingBox( SceneWorld world, Transform transform, Vector3 size, CullMode mode )
	{
		World = world;

		_boxId = 0;
		_transform = transform;
		_size = size;
		_mode = mode;

		Update();
	}

	/// <summary>
	/// Delete this culling box. You shouldn't access it anymore.
	/// </summary>
	public void Delete()
	{
		if ( !IsValid )
			return;

		if ( inNative ) CSceneSystem.RemoveCullingBox( World, _boxId );

		_boxId = 0;
		_transform = default;
		_size = default;
		_mode = default;

		World = null;
	}

	internal void Update()
	{
		if ( !World.IsValid() )
			return;

		if ( _boxId != 0 )
		{
			if ( inNative ) CSceneSystem.RemoveCullingBox( World, _boxId );

			_boxId = 0;
		}

		inNative = SceneObject.WritesNative;
		if ( !inNative )
		{
			_boxId = ++nextManagedId;
			return;
		}

		_boxId = CSceneSystem.AddCullingBox( World,
			_mode == CullMode.Inside,
			_transform.Position, _transform.Rotation.Angles(),
			_size * _transform.Scale );
	}
}
