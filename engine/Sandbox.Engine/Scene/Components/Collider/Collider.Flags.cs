namespace Sandbox;

public abstract partial class Collider
{
	ColliderFlags _flags;

	/// <summary>
	/// Flags that modify the behavior of this collider
	/// </summary>
	[Property]
	public ColliderFlags ColliderFlags
	{
		get => _flags;
		set
		{
			if ( _flags == value )
				return;

			_flags = value;
			ApplyColliderFlags();
		}
	}

	void ApplyColliderFlags()
	{
		foreach ( var shape in Shapes )
		{
			shape.IgnoreTraces = ColliderFlags.Contains( ColliderFlags.IgnoreTraces );
			shape.HasNoMass = ColliderFlags.Contains( ColliderFlags.IgnoreMass );
		}
	}
}

[Expose, Flags]
public enum ColliderFlags
{
	/// <summary>
	/// Traces can never see this collider, no matter what happens
	/// </summary>
	IgnoreTraces = 1 << 0,

	/// <summary>
	/// Collider has no mass, won't affect physics objects it collides with
	/// </summary>
	IgnoreMass = 1 << 1,
}
