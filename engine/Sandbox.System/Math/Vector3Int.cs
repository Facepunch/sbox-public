using Sandbox;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json.Serialization;

[JsonConverter( typeof( Sandbox.Internal.JsonConvert.Vector3IntConverter ) )]
[StructLayout( LayoutKind.Sequential )]
public struct Vector3Int : IEquatable<Vector3Int>, IParsable<Vector3Int>,
	System.Numerics.IAdditionOperators<Vector3Int, int, Vector3Int>,
	System.Numerics.IAdditionOperators<Vector3Int, float, Vector3>,
	System.Numerics.ISubtractionOperators<Vector3Int, int, Vector3Int>,
	System.Numerics.ISubtractionOperators<Vector3Int, float, Vector3>,
	System.Numerics.IMultiplyOperators<Vector3Int, int, Vector3Int>,
	System.Numerics.IMultiplyOperators<Vector3Int, float, Vector3>,
	System.Numerics.IDivisionOperators<Vector3Int, int, Vector3Int>,
	System.Numerics.IDivisionOperators<Vector3Int, float, Vector3>,

	System.Numerics.IAdditionOperators<Vector3Int, Vector2Int, Vector3Int>,
	System.Numerics.ISubtractionOperators<Vector3Int, Vector2Int, Vector3Int>,

	System.Numerics.IAdditionOperators<Vector3Int, Vector3Int, Vector3Int>,
	System.Numerics.ISubtractionOperators<Vector3Int, Vector3Int, Vector3Int>,
	System.Numerics.IMultiplyOperators<Vector3Int, Vector3Int, Vector3Int>,
	System.Numerics.IDivisionOperators<Vector3Int, Vector3Int, Vector3Int>,

	System.Numerics.IAdditionOperators<Vector3Int, Vector3, Vector3>,
	System.Numerics.ISubtractionOperators<Vector3Int, Vector3, Vector3>,
	System.Numerics.IMultiplyOperators<Vector3Int, Vector3, Vector3>,
	System.Numerics.IDivisionOperators<Vector3Int, Vector3, Vector3>,

	System.Numerics.IAdditionOperators<Vector3Int, Vector4, Vector4>,
	System.Numerics.ISubtractionOperators<Vector3Int, Vector4, Vector4>,

	System.Numerics.IUnaryNegationOperators<Vector3Int, Vector3Int>,

	System.Numerics.IAdditiveIdentity<Vector3Int, Vector3Int>,
	System.Numerics.IMultiplicativeIdentity<Vector3Int, Vector3Int>,
	System.Numerics.IEqualityOperators<Vector3Int, Vector3Int, bool>
{
	/// <summary>
	/// The X component of this integer vector.
	/// </summary>
	[ActionGraphInclude( AutoExpand = true )]
	[JsonInclude]
	public int x;

	/// <summary>
	/// The Y component of this integer vector.
	/// </summary>

	[ActionGraphInclude( AutoExpand = true )]
	[JsonInclude]
	public int y;

	/// <summary>
	/// The Z component of this integer vector.
	/// </summary>
	[ActionGraphInclude( AutoExpand = true )]
	[JsonInclude]
	public int z;

	/// <summary>
	/// Initializes an integer vector with given components.
	/// </summary>
	/// <param name="x">The X component.</param>
	/// <param name="y">The Y component.</param>
	/// <param name="z">The Z component.</param>
	[ActionGraphNode( "vec3int.new" ), Title( "Vector3Int" ), Group( "Math/Geometry/Vector3Int" )]
	public Vector3Int( int x, int y, int z )
	{
		this.x = x;
		this.y = y;
		this.z = z;
	}

	/// <summary>
	/// Initializes an integer vector with all components set to the same value.
	/// </summary>
	/// <param name="all">The value of the X, Y, and Z components.</param>
	public Vector3Int( int all = 0 ) : this( all, all, all )
	{
	}

	/// <summary>
	/// Initializes an integer vector with given components from another integer vector
	/// </summary>
	/// <param name="vector3Int"></param>
	public Vector3Int( Vector3Int vector3Int )
	{
		x = vector3Int.x;
		y = vector3Int.y;
		z = vector3Int.z;
	}

	/// <summary>
	/// An integer vector with all components set to 1.
	/// </summary>
	public static readonly Vector3Int One = new( 1 );

	/// <summary>
	/// An integer vector with all components set to 0.
	/// </summary>
	public static readonly Vector3Int Zero = new( 0 );

	/// <summary>
	/// An integer vector with X set to 1. This represents the forward direction.
	/// </summary>
	public static readonly Vector3Int Forward = new( 1, 0, 0 );

	/// <summary>
	/// An integer vector with X set to -1. This represents the backward direction.
	/// </summary>
	public static readonly Vector3Int Backward = new( -1, 0, 0 );

	/// <summary>
	/// An integer vector with Z set to 1. This represents the up direction.
	/// </summary>
	public static readonly Vector3Int Up = new( 0, 0, 1 );

	/// <summary>
	/// An integer vector with Z set to -1. This represents the down direction.
	/// </summary>
	public static readonly Vector3Int Down = new( 0, 0, -1 );

	/// <summary>
	/// An integer vector with Y set to 1. This represents the right direction.
	/// </summary>
	public static readonly Vector3Int Right = new( 0, -1, 0 );

	/// <summary>
	/// An integer vector with Y set to -1. This represents the left direction.
	/// </summary>
	public static readonly Vector3Int Left = new( 0, 1, 0 );

	/// <summary>
	/// An integer vector with X set to 1.
	/// </summary>
	public static readonly Vector3Int OneX = new( 1, 0, 0 );

	/// <summary>
	/// An integer vector with Y set to 1.
	/// </summary>
	public static readonly Vector3Int OneY = new( 0, 1, 0 );

	/// <summary>
	/// An integer vector with Z set to 1.
	/// </summary>
	public static readonly Vector3Int OneZ = new( 0, 0, 1 );

	/// <summary>
	/// Formats the integer vector into a string "x,y,z"
	/// </summary>
	/// <returns></returns>
	public override string ToString()
	{
		return $"{x},{y},{z}";
	}

	/// <summary>
	/// Returns a unit version of this vector. Keep in mind this returns a Vector3 and not a Vector3Int.
	/// </summary>
	public readonly Vector3 Normal
	{
		get
		{
			if ( IsZeroLength ) return this;

			return (Vector3)this / Length;
		}
	}

	/// <summary>
	/// The Euler angles of this direction vector.
	/// </summary>
	public Angles EulerAngles
	{
		readonly get { return Vector3.VectorAngle( this ); }
		set { this = (Vector3Int)Angles.AngleVector( value ); }
	}

	/// <summary>
	/// Returns the inverse of this vector, which is useful for scaling vectors. Keep in mind this returns a Vector3 and not a Vector3Int.
	/// </summary>
	public readonly Vector3 Inverse => new( 1.0f / x, 1.0f / y, 1.0f / z );

	// Vector3Int x Number Operators
	public static Vector3Int operator +( Vector3Int c1, int c2 )
	{
		return new Vector3Int( c1.x + c2, c1.y + c2, c1.z + c2 );
	}
	public static Vector3 operator +( Vector3Int c1, float c2 )
	{
		return new Vector3( c1.x + c2, c1.y + c2, c1.z + c2 );
	}
	public static Vector3Int operator -( Vector3Int c1, int c2 )
	{
		return new Vector3Int( c1.x - c2, c1.y - c2, c1.z - c2 );
	}
	public static Vector3 operator -( Vector3Int c1, float c2 )
	{
		return new Vector3( c1.x - c2, c1.y - c2, c1.z - c2 );
	}
	public static Vector3Int operator /( Vector3Int c1, int c2 )
	{
		return new Vector3Int( c1.x / c2, c1.y / c2, c1.z / c2 );
	}
	public static Vector3 operator /( Vector3Int c1, float c2 )
	{
		return new Vector3( c1.x / c2, c1.y / c2, c1.z / c2 );
	}
	public static Vector3Int operator *( Vector3Int c1, int c2 )
	{
		return new Vector3Int( c1.x * c2, c1.y * c2, c1.z * c2 );
	}
	public static Vector3 operator *( Vector3Int c1, float c2 )
	{
		return new Vector3( c1.x * c2, c1.y * c2, c1.z * c2 );
	}

	// Vector3Int x Vector2Int Operators
	public static Vector3Int operator +( Vector3Int c1, Vector2Int c2 )
	{
		return new Vector3Int( c1.x + c2.x, c1.y + c2.y, c1.z );
	}
	public static Vector3Int operator +( Vector2Int c1, Vector3Int c2 )
	{
		return new Vector3Int( c1.x + c2.x, c1.y + c2.y, c2.z );
	}
	public static Vector3Int operator -( Vector3Int c1, Vector2Int c2 )
	{
		return new Vector3Int( c1.x - c2.x, c1.y - c2.y, c1.z );
	}
	public static Vector3Int operator -( Vector2Int c1, Vector3Int c2 )
	{
		return new Vector3Int( c1.x - c2.x, c1.y - c2.y, c2.z );
	}

	// Vector3Int x Vector3Int Operators
	public static Vector3Int operator +( Vector3Int c1, Vector3Int c2 )
	{
		return new Vector3Int( c1.x + c2.x, c1.y + c2.y, c1.z + c2.z );
	}
	public static Vector3Int operator -( Vector3Int c1, Vector3Int c2 )
	{
		return new Vector3Int( c1.x - c2.x, c1.y - c2.y, c1.z - c2.z );
	}
	public static Vector3Int operator /( Vector3Int c1, Vector3Int c2 )
	{
		return new Vector3Int( c1.x / c2.x, c1.y / c2.y, c1.z / c2.z );
	}
	public static Vector3Int operator *( Vector3Int c1, Vector3Int c2 )
	{
		return new Vector3Int( c1.x * c2.x, c1.y * c2.y, c1.z * c2.z );
	}

	// Vector3Int x Vector3 Operators
	public static Vector3 operator +( Vector3Int c1, Vector3 c2 )
	{
		return new Vector3( c1.x + c2.x, c1.y + c2.y, c1.z + c2.z );
	}
	public static Vector3 operator +( Vector3 c1, Vector3Int c2 )
	{
		return new Vector3( c1.x + c2.x, c1.y + c2.y, c1.z + c2.z );
	}
	public static Vector3 operator -( Vector3Int c1, Vector3 c2 )
	{
		return new Vector3( c1.x - c2.x, c1.y - c2.y, c1.z - c2.z );
	}
	public static Vector3 operator -( Vector3 c1, Vector3Int c2 )
	{
		return new Vector3( c1.x - c2.x, c1.y - c2.y, c1.z - c2.z );
	}
	public static Vector3 operator /( Vector3Int c1, Vector3 c2 )
	{
		return new Vector3( c1.x / c2.x, c1.y / c2.y, c1.z / c2.z );
	}
	public static Vector3 operator /( Vector3 c1, Vector3Int c2 )
	{
		return new Vector3( c1.x / c2.x, c1.y / c2.y, c1.z / c2.z );
	}
	public static Vector3 operator *( Vector3Int c1, Vector3 c2 )
	{
		return new Vector3( c1.x * c2.x, c1.y * c2.y, c1.z * c2.z );
	}
	public static Vector3 operator *( Vector3 c1, Vector3Int c2 )
	{
		return new Vector3( c1.x * c2.x, c1.y * c2.y, c1.z * c2.z );
	}


	// Vector3Int x Vector4 Operators
	public static Vector4 operator +( Vector3Int c1, Vector4 c2 )
	{
		return new Vector4( c1.x + c2.x, c1.y + c2.y, c1.z + c2.z, c2.w );
	}
	public static Vector4 operator +( Vector4 c1, Vector3Int c2 )
	{
		return new Vector4( c1.x + c2.x, c1.y + c2.y, c1.z + c2.z, c1.w );
	}
	public static Vector4 operator -( Vector3Int c1, Vector4 c2 )
	{
		return new Vector4( c1.x - c2.x, c1.y - c2.y, c1.z - c2.z, c2.w );
	}
	public static Vector4 operator -( Vector4 c1, Vector3Int c2 )
	{
		return new Vector4( c1.x - c2.x, c1.y - c2.y, c1.z - c2.z, c1.w );
	}


	// Other Operators
	public static Vector3Int operator -( Vector3Int value )
	{
		return new Vector3Int( -value.x, -value.y, -value.z );
	}
	public static Vector3 operator *( Vector3Int c1, Rotation f )
	{
		return (Vector3)c1 * f;
	}

	// Implicit conversions
	static public implicit operator Vector3Int( int value )
	{
		return new Vector3Int( value, value, value );
	}
	static public implicit operator Vector3( Vector3Int value )
	{
		return new Vector3( value.x, value.y, value.z );
	}

	static public implicit operator Vector3Int( Vector2Int value )
	{
		return new Vector3Int( value.x, value.y, 0 );
	}

	// Explicit conversions
	static public explicit operator Vector3Int( Vector3 value )
	{
		return new Vector3Int( (int)value.x, (int)value.y, (int)value.z );
	}
	static public explicit operator Vector3Int( Vector2 value )
	{
		return new Vector3Int( (int)value.x, (int)value.y, 0 );
	}
	static public explicit operator Vector3Int( Vector4 value )
	{
		return new Vector3Int( (int)value.x, (int)value.y, (int)value.z );
	}

	public int this[int index]
	{
		get
		{
			return index switch
			{
				0 => x,
				1 => y,
				2 => z,
				_ => throw new IndexOutOfRangeException(),
			};
		}

		set
		{
			switch ( index )
			{
				case 0: x = value; break;
				case 1: y = value; break;
				case 2: z = value; break;
			}
		}
	}

	/// <summary>
	/// Length (or magnitude) of the integer vector (Distance from 0,0,0)
	/// </summary>
	public readonly float Length => MathF.Sqrt( x * x + y * y + z * z );

	/// <summary>
	/// Squared length of the integer vector. This is faster than <see cref="Length">Length</see>, and can be used for things like comparing distances, as long as only squared values are used.
	/// </summary>
	public readonly int LengthSquared => x * x + y * y + z * z;

	/// <summary>
	/// Whether the length of this vector is zero or not.
	/// </summary>
	public readonly bool IsZeroLength => x == 0 && y == 0 && z == 0;

	/// <summary>
	/// Returns true if value on every axis is less than or equal to tolerance.
	/// </summary>
	public readonly bool IsNearlyZero( int tolerance = 0 )
	{
		return Math.Abs( x ) <= tolerance && Math.Abs( y ) <= tolerance && Math.Abs( z ) <= tolerance;
	}

	public void Write( BinaryWriter writer )
	{
		writer.Write( x );
		writer.Write( y );
		writer.Write( z );
	}

	public readonly Vector3Int Read( BinaryReader reader )
	{
		return new Vector3Int( reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32() );
	}

	/// <summary>
	/// Returns an integer vector that has the minimum values on each axis between this vector and a given vector.
	/// </summary>
	public readonly Vector3Int ComponentMin( Vector3Int other )
	{
		return new Vector3Int( Math.Min( x, other.x ), Math.Min( y, other.y ), Math.Min( z, other.z ) );
	}

	/// <summary>
	/// Returns an integer vector that has the maximum values on each axis between this vector and a given vector.
	/// </summary>
	public readonly Vector3Int ComponentMax( Vector3Int other )
	{
		return new Vector3Int( Math.Max( x, other.x ), Math.Max( y, other.y ), Math.Max( z, other.z ) );
	}

	/// <summary>
	/// Returns the cross product of this and the given integer vector.
	/// If this and the given vectors are linearly independent, the resulting vector is perpendicular to them both, also known as a normal of a plane.
	/// </summary>
	public static Vector3Int Cross( in Vector3Int a, in Vector3Int b )
	{
		return new Vector3Int(
			a.y * b.z - a.z * b.y,
			a.z * b.x - a.x * b.z,
			a.x * b.y - a.y * b.x
		);
	}

	/// <summary>
	/// Returns the scalar/dot product of the 2 given integer vectors.
	/// </summary>
	public static float Dot( in Vector3Int a, in Vector3Int b )
	{
		return a.x * b.x + a.y * b.y + a.z * b.z;
	}

	/// <summary>
	/// Returns the scalar/dot product of the 2 given vectors.
	/// </summary>
	public static float Dot( in Vector3Int a, in Vector3 b )
	{
		return a.x * b.x + a.y * b.y + a.z * b.z;
	}

	/// <summary>
	/// Returns the scalar/dot product of this and the given vector.
	/// </summary>
	public readonly float Dot( in Vector3Int b ) => Dot( this, b );

	/// <summary>
	/// Returns the scalar/dot product of this and the given vector.
	/// </summary>
	public readonly float Dot( in Vector3 b ) => Dot( this, b );

	/// <summary>
	/// Returns distance between this vector and another.
	/// </summary>
	public float Distance( Vector3Int other )
	{
		var dx = other.x - x;
		var dy = other.y - y;
		var dz = other.z - z;
		return MathF.Sqrt( dx * dx + dy * dy + dz * dz );
	}

	/// <summary>
	/// Returns distance between this vector and another.
	/// </summary>
	public float Distance( Vector3 other )
	{
		var dx = other.x - x;
		var dy = other.y - y;
		var dz = other.z - z;
		return MathF.Sqrt( dx * dx + dy * dy + dz * dz );
	}

	/// <summary>
	/// Snap to grid along any of the 3 axes.
	/// </summary>
	public readonly Vector3Int SnapToGrid( int gridSize, bool sx = true, bool sy = true, bool sz = true )
	{
		return new Vector3Int(
			sx ? x.SnapToGrid( gridSize ) : x,
			sy ? y.SnapToGrid( gridSize ) : y,
			sz ? z.SnapToGrid( gridSize ) : z
		);
	}

	public static float GetAngle( in Vector3Int v1, in Vector3Int v2 )
	{
		return MathF.Acos( Vector3.Dot( v1.Normal, v2.Normal ).Clamp( -1, 1 ) ).RadianToDegree();
	}

	/// <summary>
	/// Returns an integer vector that has the minimum values on each axis between 2 given vectors.
	/// </summary>
	public static Vector3Int Min( Vector3Int a, Vector3Int b ) => a.ComponentMin( b );

	/// <summary>
	/// Returns an integer vector that has the maximum values on each axis between 2 given vectors.
	/// </summary>
	public static Vector3Int Max( Vector3Int a, Vector3Int b ) => a.ComponentMax( b );

	/// <summary>
	/// Returns a new integer vector with all values positive. -5 becomes 5, ect.
	/// </summary>
	public readonly Vector3Int Abs()
	{
		return new Vector3Int( Math.Abs( x ), Math.Abs( y ), Math.Abs( z ) );
	}

	/// <summary>
	/// Returns this integer vector with given X component.
	/// </summary>
	public readonly Vector3Int WithX( int x ) => new( x, y, z );

	/// <summary>
	/// Returns this integer vector with given Y component.
	/// </summary>
	public readonly Vector3Int WithY( int y ) => new( x, y, z );

	/// <summary>
	/// Returns this integer vector with given Z component.
	/// </summary>
	public readonly Vector3Int WithZ( int z ) => new( x, y, z );

	#region Equality
	public static bool operator ==( Vector3Int left, Vector3Int right ) => left.Equals( right );
	public static bool operator !=( Vector3Int left, Vector3Int right ) => !(left == right);
	public override bool Equals( object obj ) => obj is Vector3Int o && Equals( o );
	public bool Equals( Vector3Int o ) => x == o.x && y == o.y && z == o.z;
	public override int GetHashCode() => HashCode.Combine( x, y, z );
	#endregion

	#region identity
	public static Vector3Int AdditiveIdentity => Zero;
	public static Vector3Int MultiplicativeIdentity => One;
	#endregion

	/// <summary>
	/// Given a string, try to convert this into a Vector3Int. Example formatting is "x,y,z", "[x,y,z]", "x y z", etc.
	/// </summary>
	public static Vector3Int Parse( string str )
	{
		if ( TryParse( str, CultureInfo.InvariantCulture, out var res ) )
			return res;

		return default;
	}

	/// <inheritdoc cref="Parse(string)" />
	public static Vector3Int Parse( string str, IFormatProvider provider )
	{
		return Parse( str );
	}

	/// <inheritdoc cref="Parse(string)" />
	public static bool TryParse( [NotNullWhen( true )] string str, IFormatProvider info, [MaybeNullWhen( false )] out Vector3Int result )
	{
		result = Vector3Int.Zero;

		if ( string.IsNullOrWhiteSpace( str ) )
			return false;

		str = str.Trim( '[', ']', ' ', '\n', '\r', '\t', '"' );

		var components = str.Split( new[] { ' ', ',', ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries );

		if ( components.Length != 3 )
			return false;

		if ( !int.TryParse( components[0], NumberStyles.Integer, info, out int x ) ||
			!int.TryParse( components[1], NumberStyles.Integer, info, out int y ) ||
			!int.TryParse( components[2], NumberStyles.Integer, info, out int z ) )
		{
			return false;
		}

		result = new Vector3Int( x, y, z );
		return true;
	}
}
