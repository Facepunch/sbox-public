using Sandbox;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.Json.Serialization;
using Sandbox.Interpolation;

/// <summary>
/// Represents a Quaternion rotation. Can be interpreted as a direction unit vector (x,y,z) + rotation around the direction vector (w) which represents the up direction.
/// Unlike <see cref="global::Angles"/>, this cannot store multiple revolutions around an axis.
/// </summary>
[JsonConverter( typeof( Sandbox.Internal.JsonConvert.RotationConverter ) )]
[StructLayout( LayoutKind.Sequential )]
public struct Rotation : System.IEquatable<Rotation>, IParsable<Rotation>, IInterpolator<Rotation>
{
	internal System.Numerics.Quaternion _quat;

	/// <summary>
	/// The X component of this rotation.
	/// </summary>
	public float x
	{
		readonly get => _quat.X;
		set => _quat.X = value;
	}

	/// <summary>
	/// The Y component of this rotation.
	/// </summary>
	public float y
	{
		readonly get => _quat.Y;
		set => _quat.Y = value;
	}

	/// <summary>
	/// The Z component of this rotation.
	/// </summary>
	public float z
	{
		readonly get => _quat.Z;
		set => _quat.Z = value;
	}

	/// <summary>
	/// The W component of this rotation (rotation around the normal defined by X,Y,Z components).
	/// </summary>
	public float w
	{
		readonly get => _quat.W;
		set => _quat.W = value;
	}

	/// <summary>
	/// Initializes this rotation to identity.
	/// </summary>
	public Rotation()
	{
		_quat = Quaternion.Identity;
	}

	/// <summary>
	/// Initializes the rotation from given components.
	/// </summary>
	/// <param name="x">The X component.</param>
	/// <param name="y">The Y component.</param>
	/// <param name="z">The Z component.</param>
	/// <param name="w">The W component.</param>
	public Rotation( float x, float y, float z, float w )
	{
		_quat = new Quaternion( x, y, z, w );
	}

	/// <summary>
	/// Initializes the rotation from a normal vector + rotation around it.
	/// </summary>
	/// <param name="v">The normal vector.</param>
	/// <param name="w">The W component, aka rotation around the normal vector.</param>
	public Rotation( Vector3 v, float w )
	{
		_quat = new Quaternion( v.x, v.y, v.z, w );
	}

	/// <summary>
	/// The forwards direction of this rotation.
	/// </summary>
	[ActionGraphInclude( AutoExpand = true )]
	public readonly Vector3 Forward => Vector3.Forward * this;

	/// <summary>
	/// The backwards direction of this rotation.
	/// </summary>
	public readonly Vector3 Backward => Vector3.Backward * this;

	/// <summary>
	/// The right hand direction of this rotation.
	/// </summary>
	[ActionGraphInclude( AutoExpand = true )]
	public readonly Vector3 Right => Vector3.Right * this;

	/// <summary>
	/// The left hand direction of this rotation.
	/// </summary>
	public readonly Vector3 Left => Vector3.Left * this;

	/// <summary>
	/// The upwards direction of this rotation.
	/// </summary>
	[ActionGraphInclude( AutoExpand = true )]
	public readonly Vector3 Up => Vector3.Up * this;

	/// <summary>
	/// The downwards direction of this rotation.
	/// </summary>
	public readonly Vector3 Down => Vector3.Down * this;

	/// <summary>
	/// Returns the inverse of this rotation.
	/// </summary>
	public readonly Rotation Inverse => System.Numerics.Quaternion.Inverse( _quat );

	/// <summary>
	/// Divides each component of the rotation by its length, normalizing the rotation.
	/// </summary>
	public readonly Rotation Normal => System.Numerics.Quaternion.Normalize( _quat );

	/// <summary>
	/// Returns conjugate of this rotation, meaning the X Y and Z components are negated.
	/// </summary>
	public readonly Rotation Conjugate => System.Numerics.Quaternion.Conjugate( _quat );

	/// <summary>
	/// Returns a uniformly random rotation.
	/// </summary>
	[ActionGraphNode( "rotation.random" ), Title( "Random Rotation" ), Group( "Math/Geometry/Rotation" ), Icon( "casino" )]
	public static Rotation Random => SandboxSystem.Random.Rotation();

	/// <summary>
	/// Create from angle and an axis
	/// </summary>
	/// <remarks><paramref name="axis" /> vector must be normalized before calling this method or the resulting <see cref="Rotation" /> will be incorrect.</remarks>
	[ActionGraphNode( "rotation.fromaxis" ), Title( "Rotation From Axis" ), Pure, Group( "Math/Geometry/Rotation" ), Icon( "360" )]
	public static Rotation FromAxis( Vector3 axis, float degrees )
	{
		return Quaternion.CreateFromAxisAngle( axis, degrees.DegreeToRadian() );
	}

	/// <summary>
	/// Create a Rotation (quaternion) from Angles
	/// </summary>
	public static Rotation From( Angles angles )
	{
		return From( angles.pitch, angles.yaw, angles.roll );
	}

	/// <summary>
	/// Create a Rotation (quaternion) from pitch yaw roll (degrees)
	/// </summary>
	[ActionGraphNode( "rotation.from" ), Title( "Rotation From Angles" ), Pure, Group( "Math/Geometry/Rotation" ), Icon( "360" )]
	public static Rotation From( float pitch, float yaw, float roll )
	{
		Rotation rot = default;

		pitch = pitch.DegreeToRadian() * 0.5f;
		yaw = yaw.DegreeToRadian() * 0.5f;
		roll = roll.DegreeToRadian() * 0.5f;

		float sp = MathF.Sin( pitch );
		float cp = MathF.Cos( pitch );

		float sy = MathF.Sin( yaw );
		float cy = MathF.Cos( yaw );

		float sr = MathF.Sin( roll );
		float cr = MathF.Cos( roll );

		// NJS: for some reason VC6 wasn't recognizing the common subexpressions:
		float srXcp = sr * cp, crXsp = cr * sp;
		rot.x = srXcp * cy - crXsp * sy; // X
		rot.y = crXsp * cy + srXcp * sy; // Y

		float crXcp = cr * cp, srXsp = sr * sp;
		rot.z = crXcp * sy - srXsp * cy; // Z
		rot.w = crXcp * cy + srXsp * sy; // W (real component)

		return rot;
	}

	/// <summary>
	/// Create a Rotation (quaternion) from pitch (degrees)
	/// </summary>
	public static Rotation FromPitch( float pitch )
	{
		return From( pitch, 0, 0 );
	}

	/// <summary>
	/// Create a Rotation (quaternion) from yaw (degrees)
	/// </summary>
	public static Rotation FromYaw( float yaw )
	{
		return From( 0, yaw, 0 );
	}

	/// <summary>
	/// Create a Rotation (quaternion) from roll (degrees)
	/// </summary>
	public static Rotation FromRoll( float roll )
	{
		return From( 0, 0, roll );
	}

	/// <summary>
	/// Create a Rotation (quaternion) from a forward and up vector
	/// </summary>
	[ActionGraphNode( "rotation.lookat" ), Pure, Group( "Math/Geometry/Rotation" ), Icon( "visibility" )]
	public static Rotation LookAt( Vector3 forward, Vector3 up )
	{
		forward = forward.Normal;
		up = up.Normal;

		float flRatio = forward.Dot( up );

		up = (up - (forward * flRatio)).Normal;
		var right = forward.Cross( up ).Normal;

		var vX = forward;
		var vY = -right;
		var vZ = up;

		float flTrace = vX.x + vY.y + vZ.z;

		Quaternion q;

		if ( flTrace >= 0.0f )
		{
			q.X = vY.z - vZ.y;
			q.Y = vZ.x - vX.z;
			q.Z = vX.y - vY.x;
			q.W = flTrace + 1.0f;
		}
		else
		{
			if ( vX.x > vY.y && vX.x > vZ.z )
			{
				q.X = vX.x - vY.y - vZ.z + 1.0f;
				q.Y = vY.x + vX.y;
				q.Z = vZ.x + vX.z;
				q.W = vY.z - vZ.y;
			}
			else if ( vY.y > vZ.z )
			{
				q.X = vX.y + vY.x;
				q.Y = vY.y - vZ.z - vX.x + 1.0f;
				q.Z = vZ.y + vY.z;
				q.W = vZ.x - vX.z;
			}
			else
			{
				q.X = vX.z + vZ.x;
				q.Y = vY.z + vZ.y;
				q.Z = vZ.z - vX.x - vY.y + 1.0f;
				q.W = vX.y - vY.x;
			}
		}

		return Quaternion.Normalize( q );
	}

	/// <summary>
	/// Create a Rotation (quaternion) from a forward vector, using <see cref="Vector3.Up"/> as
	/// an up vector. This won't give nice results if <paramref name="forward"/> is very close to straight
	/// up or down, if that can happen you should use <see cref="LookAt(Vector3,Vector3)"/>.
	/// </summary>
	[ActionGraphNode( "rotation.lookat" ), Pure, Group( "Math/Geometry/Rotation" ), Icon( "visibility" )]
	public static Rotation LookAt( Vector3 forward )
	{
		if ( forward.WithZ( 0f ).IsNearZeroLength )
			return LookAt( forward, Vector3.Left );

		return LookAt( forward, Vector3.Up );
	}

	/// <summary>
	/// A rotation that represents no rotation.
	/// </summary>
	public static readonly Rotation Identity = new() { _quat = System.Numerics.Quaternion.Identity };

	/// <summary>
	/// Returns the difference between two rotations, as a rotation
	/// </summary>
	[ActionGraphNode( "rotation.diff" ), Pure, Group( "Math/Geometry/Rotation" ), Icon( "360" )]
	public static Rotation Difference( Rotation from, Rotation to )
	{
		var fromInv = Quaternion.Conjugate( from._quat );
		var diff = Quaternion.Multiply( to._quat, fromInv );
		return Quaternion.Normalize( diff );
	}

	/// <summary>
	/// The degree angular distance between this rotation and the target
	/// </summary>
	public readonly float Distance( Rotation to )
	{
		var diff = Difference( this, to );
		return diff.Angle();
	}

	/// <summary>
	/// Returns the turn length of this rotation (from identity) in degrees
	/// </summary>
	public readonly float Angle()
	{
		float d = MathF.Acos( w.Clamp( -1.0f, 1.0f ) ).RadianToDegree() * 2.0f;
		if ( d > 180 ) d -= 360;
		return MathF.Abs( d );
	}

	/// <summary>
	/// Return this Rotation as pitch, yaw, roll angles
	/// </summary>
	public readonly Angles Angles()
	{
		// Adapted from https://www.euclideanspace.com/maths/geometry/rotations/conversions/quaternionToEuler/

		var m13 = (2.0f * x * z) - (2.0f * w * y);

		Angles a;

		a.pitch = MathF.Asin( Math.Clamp( -m13, -1f, 1f ) ).RadianToDegree();

		if ( Math.Abs( m13 ).AlmostEqual( 1f ) )
		{
			// North / south pole singularities

			var m21 = 2f * (w * z - x * y);
			var m31 = 2f * (x * z + w * y);

			var sign = -Math.Sign( m13 );

			a.pitch = (MathF.PI / 2 * sign).RadianToDegree();
			a.yaw = sign * MathF.Atan2( m21 * sign, m31 * sign ).RadianToDegree();
			a.roll = 0f;
		}
		else
		{
			// Normal case

			var m11 = 2f * (w * w + x * x) - 1f;
			var m12 = 2f * (x * y + w * z);
			var m23 = 2f * (y * z + w * x);
			var m33 = 2f * (w * w + z * z) - 1f;

			a.yaw = MathF.Atan2( m12, m11 ).RadianToDegree();
			a.roll = MathF.Atan2( m23, m33 ).RadianToDegree();
		}

		return a;
	}

	/// <summary>
	/// Return this Rotation pitch
	/// </summary>
	public readonly float Pitch()
	{
		float m13 = (2.0f * x * z) - (2.0f * w * y);

		return MathF.Asin( Math.Clamp( -m13, -1f, 1f ) ).RadianToDegree();
	}

	/// <summary>
	/// Return this Rotation yaw
	/// </summary>
	public readonly float Yaw()
	{
		float m11 = (2.0f * w * w) + (2.0f * x * x) - 1.0f;
		float m12 = (2.0f * x * y) + (2.0f * w * z);

		return MathF.Atan2( m12, m11 ).RadianToDegree();
	}

	/// <summary>
	/// Return this Rotation roll
	/// </summary>
	public readonly float Roll()
	{
		float m23 = (2.0f * y * z) + (2.0f * w * x);
		float m33 = (2.0f * w * w) + (2.0f * z * z) - 1.0f;

		return MathF.Atan2( m23, m33 ).RadianToDegree();
	}

	/// <summary>
	/// Perform a linear interpolation from a to b by given amount.
	/// </summary>
	[ActionGraphNode( "geom.lerp" ), Pure, Group( "Math/Geometry" ), Icon( "timeline" )]
	public static Rotation Lerp( Rotation a, Rotation b, [Range( 0f, 1f )] float frac, bool clamp = true )
	{
		if ( clamp ) frac = frac.Clamp( 0, 1 );
		return Quaternion.Lerp( a._quat, b._quat, frac );
	}

	/// <summary>
	/// Perform a spherical interpolation from a to b by given amount.
	/// </summary>
	[ActionGraphNode( "geom.slerp" ), Pure, Group( "Math/Geometry/Rotation" ), Icon( "360" )]
	public static Rotation Slerp( Rotation a, Rotation b, float amount, bool clamp = true )
	{
		if ( clamp ) amount = amount.Clamp( 0, 1 );
		return Quaternion.Slerp( a._quat, b._quat, amount );
	}

	/// <summary>
	/// Perform a linear interpolation from this rotation to a target rotation by given amount.
	/// </summary>
	public readonly Rotation LerpTo( Rotation target, float frac, bool clamp = true ) => Lerp( this, target, frac, clamp );

	/// <summary>
	/// Perform a spherical interpolation from this rotation to a target rotation by given amount.
	/// </summary>
	public readonly Rotation SlerpTo( Rotation target, float frac, bool clamp = true ) => Slerp( this, target, frac, clamp );

	/// <summary>
	/// Clamp to within degrees of passed rotation
	/// </summary>
	public readonly Rotation Clamp( Rotation to, float degrees ) => Clamp( to, degrees, out var _ );

	/// <summary>
	/// Clamp to within degrees of passed rotation. Also pases out the change in degrees, if any.
	/// </summary>
	public readonly Rotation Clamp( Rotation to, float degrees, out float change )
	{
		change = 0;

		// what are you doing
		if ( degrees <= 0 ) return to;

		// Get difference
		var diff = Difference( this, to );

		// Get degrees
		var d = diff.Angle();

		// Within range, that's fine
		if ( d <= degrees ) return this;

		change = d - degrees;
		var amount = degrees / d;

		return Slerp( this, to, 1 - amount );
	}

	/// <summary>
	/// A convenience function that rotates this rotation around a given axis given amount of degrees
	/// </summary>
	/// <remarks><paramref name="axis" /> vector must be normalized before calling this method or the resulting <see cref="Rotation" /> will be incorrect.</remarks>
	public readonly Rotation RotateAroundAxis( Vector3 axis, float degrees )
	{
		return this * Rotation.FromAxis( axis, degrees );
	}

	internal static Rotation Exp( Vector3 V )
	{
		// Exponential map (Grassia)
		const float kThreshold = 0.018581361f;
		float Angle = V.Length;

		if ( Angle < kThreshold )
		{
			// Taylor expansion
			return new Rotation( (0.5f + Angle * Angle / 48.0f) * V, MathF.Cos( 0.5f * Angle ) );
		}
		else
		{
			return Quaternion.CreateFromAxisAngle( V / Angle, Angle );
		}
	}

	/// <summary>
	/// Smoothly move towards the target rotation
	/// </summary>
	[ActionGraphNode( "rotation.smoothdamp" ), Pure, Group( "Math/Geometry/Rotation" )]
	public static Rotation SmoothDamp( Rotation current, in Rotation target, ref Vector3 velocity, float smoothTime, float deltaTime )
	{
		// If smoothing time is zero, directly jump to target (independent of timestep)
		if ( smoothTime <= 0.0f )
		{
			return target;
		}

		// If timestep is zero, stay at current position
		if ( deltaTime <= 0.0f )
		{
			return current;
		}

		// Implicit integration of critically damped spring
		if ( Quaternion.Dot( current._quat, target._quat ) < 0.0f )
		{
			current = new Rotation( -current.x, -current.y, -current.z, -current.w );
		}

		var delta = Quaternion.Multiply( target._quat - current._quat, 2.0f ) * Quaternion.Conjugate( current._quat );
		var omega = MathF.PI * 2.0f / smoothTime;
		var v = new Vector3( delta.X, delta.Y, delta.Z );
		velocity = (velocity + (omega * omega) * deltaTime * v) / ((1.0f + omega * deltaTime) * (1.0f + omega * deltaTime));

		return (Exp( velocity * deltaTime ) * current).Normal;
	}

	/// <summary>
	/// Will give you the axis most aligned with the given normal
	/// </summary>
	public readonly Vector3 ClosestAxis( Vector3 normal )
	{
		normal = normal.Normal;

		var axis = new Vector3[6];
		axis[0] = Forward;
		axis[1] = Left;
		axis[2] = Up;
		axis[3] = -axis[0];
		axis[4] = -axis[1];
		axis[5] = -axis[2];

		var bestAxis = Vector3.Zero;
		var bestDot = -1.0f;

		for ( var i = 0; i < 6; i++ )
		{
			var dot = normal.Dot( axis[i] );
			if ( dot > bestDot )
			{
				bestDot = dot;
				bestAxis = axis[i];
			}
		}

		return bestAxis;
	}


	/// <summary>
	/// Returns a Rotation that rotates from one direction to another.
	/// </summary>
	public static Rotation FromToRotation( in Vector3 fromDirection, in Vector3 toDirection )
	{
		Vector3 axis = Vector3.Cross( fromDirection, toDirection );
		float angle = Vector3.GetAngle( fromDirection, toDirection );
		return FromAxis( axis.Normal, angle );
	}

	/// <summary>
	/// Given a string, try to convert this into a quaternion rotation. The format is "x,y,z,w"
	/// </summary>
	public static Rotation Parse( string str )
	{
		if ( TryParse( str, CultureInfo.InvariantCulture, out var res ) )
			return res;

		return default;
	}

	/// <inheritdoc cref="Parse(string)" />
	public static Rotation Parse( string str, IFormatProvider provider )
	{
		return Parse( str );
	}

	/// <inheritdoc cref="Parse(string)" />
	public static bool TryParse( string str, out Rotation result )
	{
		return TryParse( str, CultureInfo.InvariantCulture, out result );
	}

	/// <inheritdoc cref="Parse(string)" />
	public static bool TryParse( [NotNullWhen( true )] string str, IFormatProvider provider, [MaybeNullWhen( false )] out Rotation result )
	{
		result = Identity;

		if ( string.IsNullOrWhiteSpace( str ) )
			return false;

		str = str.Trim( '[', ']', ' ', '\n', '\r', '\t', '"' );

		var components = str.Split( new[] { ' ', ',', ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries );

		if ( components.Length == 4 )
		{
			// Try to parse as a Rotation (x, y, z, w)
			if ( float.TryParse( components[0], NumberStyles.Float, provider, out float x ) &&
				float.TryParse( components[1], NumberStyles.Float, provider, out float y ) &&
				float.TryParse( components[2], NumberStyles.Float, provider, out float z ) &&
				float.TryParse( components[3], NumberStyles.Float, provider, out float w ) )
			{
				result = new Rotation( x, y, z, w );
				return true;
			}
		}
		else if ( components.Length == 3 )
		{
			// Try to parse as Euler angles (pitch, yaw, roll)
			if ( float.TryParse( components[0], NumberStyles.Float, provider, out float pitch ) &&
				float.TryParse( components[1], NumberStyles.Float, provider, out float yaw ) &&
				float.TryParse( components[2], NumberStyles.Float, provider, out float roll ) )
			{
				var angles = new Angles( pitch, yaw, roll );
				result = angles.ToRotation();
				return true;
			}
		}

		return false;
	}

	public override readonly string ToString()
	{
		return $"{x:0.#####},{y:0.#####},{z:0.#####},{w:0.#####}";
	}

	#region operators
	public static implicit operator Rotation( in System.Numerics.Quaternion value )
	{
		return new Rotation { _quat = value };
	}

	public static implicit operator Rotation( in Angles value ) => From( value );
	public static implicit operator Angles( in Rotation value ) => value.Angles();

	public static implicit operator System.Numerics.Quaternion( in Rotation value )
	{
		return new System.Numerics.Quaternion( value.x, value.y, value.z, value.w );
	}

	public static Vector3 operator *( in Rotation f, in Vector3 c1 )
	{
		return System.Numerics.Vector3.Transform( c1._vec, f._quat );
	}

	public static Rotation operator *( Rotation a, Rotation b )
	{
		return Quaternion.Multiply( a._quat, b._quat );
	}

	public static Rotation operator *( Rotation a, float f )
	{
		return Quaternion.Slerp( Quaternion.Identity, a._quat, f );
	}

	public static Rotation operator /( Rotation a, float f )
	{
		return Quaternion.Slerp( Quaternion.Identity, a._quat, 1 / f );
	}

	[Obsolete( "Use the * operator if you want to combine rotations. If you really want to add (+) them use quaternions." )]
	public static Rotation operator +( Rotation a, Rotation b )
	{
		return Quaternion.Add( a._quat, b._quat );
	}

	[Obsolete( "Use Rotation.Difference if you want to get the delta between rotations. If you really want to subtract (-) them use quaternions." )]
	public static Rotation operator -( Rotation a, Rotation b )
	{
		return Quaternion.Subtract( a._quat, b._quat );
	}
	#endregion

	#region equality
	public static bool operator ==( Rotation left, Rotation right ) => left.Equals( right );
	public static bool operator !=( Rotation left, Rotation right ) => !(left == right);
	public readonly override bool Equals( object obj ) => obj is Rotation o && Equals( o );
	public readonly bool Equals( Rotation o ) => _quat.X.AlmostEqual( o._quat.X, 0.000001f ) && _quat.Y.AlmostEqual( o._quat.Y, 0.000001f ) && _quat.Z.AlmostEqual( o._quat.Z, 0.000001f ) && _quat.W.AlmostEqual( o._quat.W, 0.000001f );
	public readonly override int GetHashCode() => HashCode.Combine( _quat );

	/// <summary>
	/// Returns true if we're nearly equal to the passed rotation.
	/// </summary>
	/// <param name="r">The value to compare with</param>
	/// <param name="delta">The max difference between component values</param>
	/// <returns>True if nearly equal</returns>
	public readonly bool AlmostEqual( in Rotation r, float delta = 0.0001f )
	{
		return Quaternion.Dot( _quat, r._quat ) > 1.0f - delta;
	}
	#endregion

	Rotation IInterpolator<Rotation>.Interpolate( Rotation a, Rotation b, float delta )
	{
		return a.LerpTo( b, delta );
	}
}
