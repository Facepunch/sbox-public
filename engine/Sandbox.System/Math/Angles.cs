using Sandbox;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json.Serialization;
using Sandbox.Interpolation;

/// <summary>
/// Euler angles. Unlike a <see cref="Rotation">Rotation</see>, Euler angles can represent multiple revolutions (rotations) around an axis,
/// but suffer from issues like gimbal lock and lack of a defined "up" vector. Use <see cref="Rotation">Rotation</see> for most cases.
/// </summary>
[JsonConverter( typeof( Sandbox.Internal.JsonConvert.AnglesConverter ) )]
[StructLayout( LayoutKind.Sequential )]
public struct Angles : IEquatable<Angles>, IParsable<Angles>, IInterpolator<Angles>
{
	/// <summary>
	/// The pitch component, typically up/down.
	/// </summary>
	public float pitch;

	/// <summary>
	/// The yaw component, typically left/right.
	/// </summary>
	public float yaw;

	/// <summary>
	/// The roll component, basically rotation around the axis.
	/// </summary>
	public float roll;

	/// <summary>
	/// Initializes the angles object with given components.
	/// </summary>
	/// <param name="pitch">The Pitch component.</param>
	/// <param name="yaw">The Yaw component.</param>
	/// <param name="roll">The roll component.</param>
	[ActionGraphNode( "angles.new" ), Title( "Euler Angles" ), Group( "Math/Geometry/Angles" )]
	public Angles( float pitch, float yaw, float roll )
	{
		this.pitch = pitch;
		this.yaw = yaw;
		this.roll = roll;
	}

	/// <summary>
	/// Copies values of given angles object.
	/// </summary>
	public Angles( Angles other )
	{
		this.pitch = other.pitch;
		this.yaw = other.yaw;
		this.roll = other.roll;
	}

	/// <summary>
	/// Where x, y and z represent the pitch, yaw and roll respectively.
	/// </summary>
	public Angles( Vector3 vector )
	{
		this.pitch = vector.x;
		this.yaw = vector.y;
		this.roll = vector.z;
	}

	/// <summary>
	/// Initializes the angles object with all components set to given value.
	/// </summary>
	public Angles( float all = 0.0f ) : this( all, all, all )
	{
	}

	/// <summary>
	/// Converts these Euler angles to a rotation. The angles will be normalized.
	/// </summary>
	/// <returns></returns>
	public readonly Rotation ToRotation()
	{
		return Rotation.From( this );
	}

	/// <summary>
	/// Return as a Vector3, where x = pitch etc
	/// </summary>
	public readonly Vector3 AsVector3()
	{
		return new Vector3( pitch, yaw, roll );
	}

	public readonly override string ToString()
	{
		return $"Pitch = {pitch:0.00}, Yaw = {yaw:0.00}, Roll = {roll:0.00}";
	}

	/// <summary>
	/// An angle constant that has all its values set to 0. Use this instead of making a static 0,0,0 object yourself.
	/// </summary>
	public static readonly Angles Zero = new( 0 );

	/// <summary>
	/// Returns the angles of a uniformly random rotation.
	/// </summary>
	[ActionGraphNode( "angles.random" ), Title( "Random Angles" ), Group( "Math/Geometry/Angles" )]
	public static Angles Random => Rotation.Random.Angles();

	/// <summary>
	/// Returns true if this angles object's components are all nearly zero with given tolerance.
	/// </summary>
	public readonly bool IsNearlyZero( double tolerance = 0.000001 )
	{
		return MathF.Abs( pitch ) <= tolerance &&
			   MathF.Abs( yaw ) <= tolerance &&
			   MathF.Abs( roll ) <= tolerance;
	}

	/// <summary>
	/// Returns this angles object with given pitch component.
	/// </summary>
	public readonly Angles WithPitch( float pitch ) => new Angles( pitch, yaw, roll );

	/// <summary>
	/// Returns this angles object with given yaw component.
	/// </summary>
	public readonly Angles WithYaw( float yaw ) => new Angles( pitch, yaw, roll );

	/// <summary>
	/// Returns this angles object with given roll component.
	/// </summary>
	public readonly Angles WithRoll( float roll ) => new Angles( pitch, yaw, roll );

	/// <summary>
	/// Given a string, try to convert this into an angles object. The format is "p,y,r".
	/// </summary>
	public static Angles Parse( string str )
	{
		if ( TryParse( str, CultureInfo.InvariantCulture, out var res ) )
			return res;

		return default;
	}

	/// <inheritdoc cref="Parse(string)" />
	public static Angles Parse( string str, IFormatProvider provider )
	{
		return Parse( str );
	}

	/// <inheritdoc cref="Parse(string)" />
	public static bool TryParse( string str, out Angles result )
	{
		return TryParse( str, CultureInfo.InvariantCulture, out result );
	}

	/// <inheritdoc cref="Parse(string)" />
	public static bool TryParse( [NotNullWhen( true )] string str, IFormatProvider provider, [MaybeNullWhen( false )] out Angles result )
	{
		result = Angles.Zero;

		if ( string.IsNullOrWhiteSpace( str ) )
			return false;

		str = str.Trim( '[', ']', ' ', '\n', '\r', '\t', '"' );

		var components = str.Split( new[] { ' ', ',', ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries );

		if ( components.Length != 3 )
			return false;

		if ( !float.TryParse( components[0], NumberStyles.Float, provider, out float p ) ||
			!float.TryParse( components[1], NumberStyles.Float, provider, out float y ) ||
			!float.TryParse( components[2], NumberStyles.Float, provider, out float r ) )
		{
			return false;
		}

		result = new Angles( p, y, r );
		return true;
	}

	/// <summary>
	/// Returns clamped version of this object, meaning the angle on each axis is transformed to range of [0,360).
	/// </summary>
	public readonly Angles Clamped()
	{
		return new Angles( ClampAngle( pitch ), ClampAngle( yaw ), ClampAngle( roll ) );
	}

	/// <summary>
	/// Returns normalized version of this object, meaning the angle on each axis is normalized to range of (-180,180].
	/// </summary>
	public readonly Angles Normal => new Angles( NormalizeAngle( pitch ), NormalizeAngle( yaw ), NormalizeAngle( roll ) );

	/// <summary>
	/// Clamps the angle to range of [0, 360)
	/// </summary>
	public static float ClampAngle( float v )
	{
		v %= 360.0f;
		return (v < 0.0f) ? v + 360.0f : v;
	}

	/// <summary>
	/// Normalizes the angle to range of (-180, 180]
	/// </summary>
	public static float NormalizeAngle( float v )
	{
		v = ClampAngle( v );
		return (v > 180.0f) ? v - 360.0f : v;
	}

	/// <summary>
	/// Performs linear interpolation on the two given angle objects.
	/// </summary>
	/// <param name="source">Angle A</param>
	/// <param name="target">Angle B</param>
	/// <param name="frac">Fraction in range [0,1] between the 2 angle objects to use for interpolation.</param>
	public static Angles Lerp( in Angles source, in Angles target, float frac )
	{
		return source + (target - source).Normal * frac;
	}

	/// <summary>
	/// Performs linear interpolation on the two given angle objects.
	/// </summary>
	/// <param name="target">Angle B</param>
	/// <param name="frac">Fraction in range [0,1] between the 2 angle objects to use for interpolation.</param>
	public readonly Angles LerpTo( Angles target, float frac ) => Lerp( this, target, frac );

	/// <summary>
	/// Converts an angle to a forward vector.
	/// </summary>
	public static Vector3 AngleVector( Angles ang )
	{
		const float piOver180 = (float)(Math.PI / 180.0);

		float[] vAngles = { ang.yaw, ang.pitch };
		float[] vSines = new float[2];
		float[] vCosines = new float[2];

		vSines[0] = (float)Math.Sin( vAngles[0] * piOver180 );
		vSines[1] = (float)Math.Sin( vAngles[1] * piOver180 );

		vCosines[0] = (float)Math.Cos( vAngles[0] * piOver180 );
		vCosines[1] = (float)Math.Cos( vAngles[1] * piOver180 );

		return new Vector3( vCosines[1] * vCosines[0],
							vCosines[1] * vSines[0],
							-vSines[1] );
	}

	/// <summary>
	/// The forward direction vector for this angle.
	/// </summary>
	public Vector3 Forward
	{
		readonly get { return AngleVector( this ); }
		set { this = Vector3.VectorAngle( value ); }
	}

	/// <summary>
	/// Snap to grid
	/// </summary>
	public readonly Angles SnapToGrid( float gridSize, bool sx = true, bool sy = true, bool sz = true )
	{
		return new Angles( sx ? pitch.SnapToGrid( gridSize ) : pitch, sy ? yaw.SnapToGrid( gridSize ) : yaw, sz ? roll.SnapToGrid( gridSize ) : roll );
	}

	#region operators
	public static Angles operator +( Angles c1, Angles c2 )
	{
		return new Angles( c1.pitch + c2.pitch, c1.yaw + c2.yaw, c1.roll + c2.roll );
	}

	public static Angles operator +( Angles c1, Vector3 c2 )
	{
		return new Angles( c1.pitch + c2.x, c1.yaw + c2.y, c1.roll + c2.z );
	}

	public static Angles operator -( Angles c1, Angles c2 )
	{
		return new Angles( c1.pitch - c2.pitch, c1.yaw - c2.yaw, c1.roll - c2.roll );
	}

	public static Angles operator *( Angles c1, float c2 ) => new Angles( c1.pitch * c2, c1.yaw * c2, c1.roll * c2 );
	public static Angles operator /( Angles c1, float c2 ) => new Angles( c1.pitch / c2, c1.yaw / c2, c1.roll / c2 );
	#endregion

	#region equality
	public static bool operator ==( in Angles left, in Angles right ) => left.Equals( right );
	public static bool operator !=( in Angles left, in Angles right ) => !(left == right);

	public override readonly bool Equals( object obj ) => obj is Angles o && Equals( o );
	public readonly bool Equals( Angles o ) => (pitch, yaw, roll) == (o.pitch, o.yaw, o.roll);
	public readonly override int GetHashCode() => HashCode.Combine( pitch, yaw, roll );

	#endregion

	Angles IInterpolator<Angles>.Interpolate( Angles a, Angles b, float delta )
	{
		return a.LerpTo( b, delta );
	}
}
