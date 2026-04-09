using Sandbox;
using Sandbox.Interpolation;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.Json.Serialization;

/// <summary>
/// A struct containing a position, rotation and scale. This is commonly used in engine to describe
/// entity position, bone position and scene object position.
/// </summary>
[StructLayout( LayoutKind.Sequential )]
public struct Transform : System.IEquatable<Transform>, IInterpolator<Transform>
{
	/// <summary>
	/// Represents a zero transform, that being, a transform with scale of 1, position of <see cref="Vector3.Zero"/> and rotation of <see cref="Rotation.Identity"/>.
	/// </summary>
	public readonly static Transform Zero = new Transform( Vector3.Zero );

	/// <summary>
	/// Position of the transform.
	/// </summary>
	[JsonInclude, JsonPropertyName( "Position" ), ActionGraphInclude( AutoExpand = true )]
	public Vector3 Position;

	/// <summary>
	/// Scale of the transform. Does not itself scale <see cref="Position"/> or <see cref="Rotation"/>.
	/// </summary>
	[JsonInclude, JsonPropertyName( "Scale" ), ActionGraphInclude( AutoExpand = true )]
	public Vector3 Scale;

	/// <summary>
	/// A uniform scale component. Generally the scale is uniform, and we'll just access the .x component.
	/// </summary>
	[JsonIgnore]
	public float UniformScale
	{
		readonly get => Scale.x;
		set => Scale = value;
	}

	/// <summary>
	/// Rotation of this transform.
	/// </summary>
	[JsonInclude, JsonPropertyName( "Rotation" ), ActionGraphInclude( AutoExpand = true )]
	public Rotation Rotation;

	[JsonIgnore] public readonly Vector3 Forward => Rotation.Forward;
	[JsonIgnore] public readonly Vector3 Backward => Rotation.Backward;
	[JsonIgnore] public readonly Vector3 Up => Rotation.Up;
	[JsonIgnore] public readonly Vector3 Down => Rotation.Down;
	[JsonIgnore] public readonly Vector3 Right => Rotation.Right;
	[JsonIgnore] public readonly Vector3 Left => Rotation.Left;

	/// <summary>
	/// This scale can be used if you need to divide by scale without resulting in infinity or NaN values.
	/// </summary>
	[JsonIgnore]
	internal readonly Vector3 SafeScale => new( Scale.x != 0f ? Scale.x : 1f, Scale.y != 0f ? Scale.y : 1f, Scale.z != 0f ? Scale.z : 1f );

	public Transform( Vector3 pos = default )
	{
		Position = pos;
		Rotation = Rotation.Identity;
		Scale = 1.0f;
	}

	public Transform() : this( default )
	{
	}

	public Transform( Vector3 position, Rotation rotation, float scale = 1.0f )
	{
		Position = position;
		Rotation = rotation;
		Scale = scale;
	}

	public Transform( Vector3 position, Rotation rotation, Vector3 scale )
	{
		Position = position;
		Rotation = rotation;
		Scale = scale;
	}

	/// <summary>
	/// Returns true if position, scale and rotation are valid
	/// </summary>
	[JsonIgnore]
	public readonly bool IsValid
	{
		get
		{
			if ( Position.IsNaN ) return false;
			if ( Scale.IsNaN ) return false;

			// todo - how to dertermine if rotation is valid 
			if ( Rotation.x == 0 && Rotation.y == 0 && Rotation.z == 0 && Rotation.w == 0 ) return false;

			return true;
		}
	}

	/// <summary>
	/// Convert a point in world space to a point in this transform's local space
	/// </summary>
	public readonly Vector3 PointToLocal( in Vector3 worldPoint )
	{
		return Rotation.Inverse * (worldPoint - Position) / SafeScale;
	}

	/// <summary>
	/// Convert a world normal to a local normal
	/// </summary>
	public readonly Vector3 NormalToLocal( in Vector3 worldNormal )
	{
		return (Rotation.Inverse * worldNormal).Normal;
	}

	/// <summary>
	/// Convert a world rotation to a local rotation
	/// </summary>
	public readonly Rotation RotationToLocal( in Rotation worldRot )
	{
		return Rotation.Inverse * worldRot;
	}

	/// <summary>
	/// Convert a point in this transform's local space to a point in world space
	/// </summary>
	public readonly Vector3 PointToWorld( in Vector3 localPoint )
	{
		return Position + (Rotation * (localPoint * Scale));
	}

	/// <summary>
	/// Convert a local normal to a world normal
	/// </summary>
	public readonly Vector3 NormalToWorld( in Vector3 localNormal )
	{
		return (Rotation * localNormal).Normal;
	}

	/// <summary>
	/// Convert a local rotation to a world rotation
	/// </summary>
	public readonly Rotation RotationToWorld( in Rotation localRotation )
	{
		return Rotation * localRotation;
	}

	/// <summary>
	/// Convert child transform from the world to a local transform
	/// </summary>
	public Transform ToLocal( in Transform child )
	{
		var rotInv = Rotation.Inverse;

		var localSafeScale = new Vector3(
			Scale.x != 0f ? child.Scale.x / Scale.x : child.Scale.x,
			Scale.y != 0f ? child.Scale.y / Scale.y : child.Scale.y,
			Scale.z != 0f ? child.Scale.z / Scale.z : child.Scale.z
		);

		return new Transform
		{
			Position = ((child.Position - Position) * rotInv) / SafeScale,
			Rotation = rotInv * child.Rotation,
			Scale = localSafeScale
		};
	}

	/// <summary>
	/// Convert child transform from local to the world
	/// </summary>
	public readonly Transform ToWorld( in Transform child )
	{
		return new Transform
		{
			Position = ((child.Position * Scale) * Rotation) + Position,
			Rotation = Rotation * child.Rotation,
			Scale = Scale * child.Scale
		};
	}

	/// <summary>
	/// Perform linear interpolation from one transform to another.
	/// </summary>
	public static Transform Lerp( in Transform a, in Transform b, float t, bool clamp )
	{
		return new Transform
		{
			Position = Vector3.Lerp( a.Position, b.Position, t, clamp ),
			Rotation = Rotation.Slerp( a.Rotation, b.Rotation, t, clamp ),
			Scale = Vector3.Lerp( a.Scale, b.Scale, t, clamp ),
		};
	}

	/// <summary>
	/// Linearly interpolate from this transform to given transform.
	/// </summary>
	public readonly Transform LerpTo( in Transform target, float t, bool clamp = true )
	{
		return Lerp( this, target, t, clamp );
	}

	/// <summary>
	/// Add a position to this transform and return the result.
	/// </summary>
	public readonly Transform Add( in Vector3 position, bool worldSpace )
	{
		var t = this;

		if ( worldSpace ) t.Position += position;
		else t.Position = PointToWorld( position );

		return t;
	}

	/// <summary>
	/// Return this transform with a new position.
	/// </summary>
	public readonly Transform WithPosition( in Vector3 position )
	{
		var t = this;
		t.Position = position;
		return t;
	}

	/// <summary>
	/// Return this transform with a new position and rotation
	/// </summary>
	public readonly Transform WithPosition( in Vector3 position, in Rotation rotation )
	{
		var t = this;
		t.Position = position;
		t.Rotation = rotation;
		return t;
	}

	/// <summary>
	/// Return this transform with a new rotation.
	/// </summary>
	public readonly Transform WithRotation( in Rotation rotation )
	{
		var t = this;
		t.Rotation = rotation;
		return t;
	}

	/// <summary>
	/// Return this transform with a new scale.
	/// </summary>
	public readonly Transform WithScale( float scale )
	{
		var t = this;
		t.Scale = scale;
		return t;
	}

	/// <summary>
	/// Return this transform with a new scale.
	/// </summary>
	public readonly Transform WithScale( in Vector3 scale )
	{
		var t = this;
		t.Scale = scale;
		return t;
	}

	/// <summary>
	/// Create a transform that is the mirror of this
	/// </summary>
	public readonly Transform Mirror( in Sandbox.Plane plane )
	{
		// Mirror position
		Vector3 mirroredPos = plane.ReflectPoint( Position );

		// Reflect forward and up
		Vector3 forward = plane.ReflectDirection( Rotation.Forward );
		Vector3 up = plane.ReflectDirection( Rotation.Up );

		Rotation mirroredRot = Rotation.LookAt( forward, up );

		return new Transform( mirroredPos, mirroredRot, Scale );
	}

	/// <summary>
	/// Return a ray from this transform, which goes from the center along the Forward
	/// </summary>
	[JsonIgnore]
	public readonly Ray ForwardRay => new Ray( Position, Rotation.Forward );

	/// <summary>
	/// Rotate this transform around given point by given rotation and return the result.
	/// </summary>
	/// <param name="center">Point to rotate around.</param>
	/// <param name="rot">How much to rotate by. <see cref="Rotation.FromAxis(Vector3, float)"/> can be useful.</param>
	/// <returns>The rotated transform.</returns>
	public readonly Transform RotateAround( in Vector3 center, in Rotation rot )
	{
		Transform trans = this;

		var dir = trans.Position - center;
		dir = rot * dir;
		trans.Position = center + dir;

		var myRot = trans.Rotation;
		trans.Rotation *= myRot.Inverse * rot * myRot;

		return trans;
	}

	/// <summary>
	/// Concatenate (add together) the 2 given transforms and return a new resulting transform.
	/// </summary>
	public static Transform Concat( Transform parent, Transform local )
	{
		return new Transform
		{
			Position = parent.Position + parent.Scale * (parent.Rotation * local.Position),
			Scale = parent.Scale * local.Scale,
			Rotation = parent.Rotation * (parent.Rotation.Distance( local.Rotation ) >= 180 ? local.Rotation.Inverse : local.Rotation),
		};
	}

	/// <summary>
	/// Given a string, try to convert this into a transform. The format is <c>"px,py,pz,rx,ry,rz,rw"</c>.
	/// </summary>
	public static Transform Parse( string str )
	{
		str = str.Trim( '[', ']', ' ', '\n', '\r', '\t', '"' );

		var p = str.Split( new[] { ' ', ',', ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries );
		if ( p.Length != 7 ) return default;

		return new Transform( new Vector3( p[0].ToFloat(), p[1].ToFloat(), p[2].ToFloat() ), new Quaternion( p[3].ToFloat(), p[4].ToFloat(), p[5].ToFloat(), p[6].ToFloat() ) );
	}

	/// <summary>
	/// Formats the Transform into a string "pos, rot, scale"
	/// </summary>
	public override readonly string ToString()
	{
		return $"pos {Position}, rot {Rotation}, scale {Scale}";
	}

	#region equality
	public static bool operator ==( Transform left, Transform right ) => left.AlmostEqual( right );
	public static bool operator !=( Transform left, Transform right ) => !left.AlmostEqual( right );
	public readonly override bool Equals( object obj ) => obj is Transform o && Equals( o );
	public readonly bool Equals( Transform o ) => Position.Equals( o.Position ) && Scale.Equals( o.Scale ) && Rotation.Equals( o.Rotation );
	public readonly override int GetHashCode() => HashCode.Combine( Position, Scale, Rotation );

	/// <summary>
	/// Returns true if we're nearly equal to the passed transform.
	/// </summary>
	/// <param name="tx">The value to compare with</param>
	/// <param name="delta">The max difference between component values (used for Position and Scale)</param>
	/// <returns>True if nearly equal</returns>
	public readonly bool AlmostEqual( in Transform tx, float delta = 0.0001f )
	{
		return Position.AlmostEqual( tx.Position, delta ) && Scale.AlmostEqual( tx.Scale, delta ) && Rotation.AlmostEqual( tx.Rotation );
	}
	#endregion

	Transform IInterpolator<Transform>.Interpolate( Transform a, Transform b, float delta )
	{
		return Lerp( a, b, delta, true );
	}
}
