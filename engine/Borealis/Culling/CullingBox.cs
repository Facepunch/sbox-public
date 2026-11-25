namespace Borealis.Culling;

/// <summary>
/// A box that hides a world's drawn objects (<see cref="RenderWorld.CullingBoxes"/>): one that culls inside hides any object
/// whose bounds are fully inside it; once a world has any that cull outside, an object whose bounds touch none of them is
/// hidden. Lights and environment maps aren't culled by them.
/// </summary>
public readonly struct CullingBox
{
	readonly Vector3 _origin;
	readonly Vector3 _forward, _left, _up;
	readonly Vector3 _half;

	/// <summary>Whether it hides what's inside it; otherwise it's one of the boxes outside all of which objects are hidden.</summary>
	public bool CullsInside { get; }

	/// <summary>A box of <paramref name="size"/> (whole lengths along its axes, the transform's scale applied) centred on the transform's position.</summary>
	public CullingBox( Transform transform, Vector3 size, bool cullsInside )
	{
		_origin = transform.Position;
		_forward = transform.Rotation.Forward;
		_left = transform.Rotation.Left;
		_up = transform.Rotation.Up;
		_half = size * transform.Scale * 0.5f;
		CullsInside = cullsInside;
	}

	// How far the bounds' centre is along an axis from the box's, and how far the bounds reach along it
	static void Along( Vector3 axis, Vector3 offset, Vector3 extents, out float distance, out float reach )
	{
		distance = MathF.Abs( Vector3.Dot( axis, offset ) );
		reach = MathF.Abs( axis.x ) * extents.x + MathF.Abs( axis.y ) * extents.y + MathF.Abs( axis.z ) * extents.z;
	}

	/// <summary>Whether bounds (centre and half extents) are fully inside the box.</summary>
	public bool Contains( Vector3 center, Vector3 extents )
	{
		var offset = center - _origin;
		Along( _forward, offset, extents, out var d, out var r );
		if ( d + r > _half.x ) return false;
		Along( _left, offset, extents, out d, out r );
		if ( d + r > _half.y ) return false;
		Along( _up, offset, extents, out d, out r );
		return d + r <= _half.z;
	}

	/// <summary>
	/// Whether bounds (centre and half extents) touch the box or are inside it, tested against its six faces as planes (as
	/// a frustum is): bounds off a corner can count as touching.
	/// </summary>
	public bool Touches( Vector3 center, Vector3 extents )
	{
		var offset = center - _origin;
		Along( _forward, offset, extents, out var d, out var r );
		if ( d - r > _half.x ) return false;
		Along( _left, offset, extents, out d, out r );
		if ( d - r > _half.y ) return false;
		Along( _up, offset, extents, out d, out r );
		return d - r <= _half.z;
	}

	/// <summary>
	/// Whether a world's boxes hide bounds (centre and half extents): fully inside one that culls inside, or, with any that
	/// cull outside, touching none of those.
	/// </summary>
	public static bool Culls( ReadOnlySpan<CullingBox> boxes, Vector3 center, Vector3 extents )
	{
		var outside = false;
		foreach ( ref readonly var box in boxes )
		{
			if ( box.CullsInside )
			{
				if ( box.Contains( center, extents ) ) return true;
			}
			else outside = true;
		}

		if ( !outside ) return false;
		foreach ( ref readonly var box in boxes )
		{
			if ( !box.CullsInside && box.Touches( center, extents ) ) return false;
		}

		return true;
	}

	/// <summary>Whether culling boxes hide objects of this kind: everything drawn, not lights or environment maps.</summary>
	internal static bool CullsObject( RenderObject obj ) => obj is not LightObject and not EnvMapObject;
}
