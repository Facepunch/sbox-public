namespace Sandbox;

/// <summary>
/// A surface that wants sharp planar reflections: water, a mirror, a wet floor. Like a scene object it lives in a
/// <see cref="SceneWorld"/>, and its owner keeps it updated and deletes it. Each top-level view renders at most one
/// planar reflection, for the plane covering the most of it, as it starts (<see cref="Rendering.PlanarReflections"/>),
/// and planes lying on one plane share it. Materials on that plane sample it from the view's attributes.
/// </summary>
internal sealed class SceneReflectionPlane
{
	/// <summary>
	/// What views read, replaced whole so a render job never sees half an update.
	/// </summary>
	/// <param name="Transform">A point on the plane, and a rotation whose up axis is the plane's normal.</param>
	/// <param name="Size">The reflecting area's extent along the rotation's forward and left axes.</param>
	/// <param name="Infinite">Reflects everywhere on the plane, ignoring <paramref name="Size"/>.</param>
	/// <param name="ClipOffset">How far beneath the plane the reflection's clip plane sits, for surfaces that move, like waves.</param>
	/// <param name="Downsample">Divides the reflection's resolution, 1 to 8.</param>
	/// <param name="ExcludeTag">Objects with this tag, like the reflecting surface itself, aren't drawn in the reflection.</param>
	internal sealed record State( Transform Transform, Vector2 Size, bool Infinite, float ClipOffset, int Downsample, string ExcludeTag )
	{
		public Vector3 Normal => Transform.Rotation.Up;
		public Vector3 Position => Transform.Position;
	}

	State _state;

	public SceneWorld World { get; private set; }

	public bool IsValid => World is not null;

	internal State Current => System.Threading.Volatile.Read( ref _state );

	public SceneReflectionPlane( SceneWorld world )
	{
		ArgumentNullException.ThrowIfNull( world );
		World = world;

		lock ( world.ReflectionPlanes )
			world.ReflectionPlanes.Add( this );
	}

	/// <summary>
	/// Set what views reflect. Unchanged values don't replace the state.
	/// </summary>
	public void Update( Transform transform, Vector2 size, bool infinite, float clipOffset, int downsample, string excludeTag )
	{
		var current = Current;
		if ( current is not null && current.Transform == transform && current.Size == size && current.Infinite == infinite
			&& current.ClipOffset == clipOffset && current.Downsample == downsample && current.ExcludeTag == excludeTag )
			return;

		System.Threading.Volatile.Write( ref _state, new State( transform, size, infinite, clipOffset, Math.Clamp( downsample, 1, 8 ), excludeTag ) );
	}

	public void Delete()
	{
		var world = World;
		if ( world is null )
			return;

		lock ( world.ReflectionPlanes )
			world.ReflectionPlanes.Remove( this );

		World = null;
	}
}

public sealed partial class SceneWorld
{
	/// <summary>
	/// Surfaces that want planar reflections. Lock it to read or change it.
	/// </summary>
	internal List<SceneReflectionPlane> ReflectionPlanes { get; } = [];

	/// <summary>
	/// The current state of every reflection plane that has one.
	/// </summary>
	internal void GetReflectionPlanes( List<SceneReflectionPlane.State> planes )
	{
		lock ( ReflectionPlanes )
		{
			foreach ( var plane in ReflectionPlanes )
			{
				if ( plane.Current is { } state )
					planes.Add( state );
			}
		}
	}
}
