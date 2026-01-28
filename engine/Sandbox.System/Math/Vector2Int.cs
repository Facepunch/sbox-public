using Sandbox;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json.Serialization;

[JsonConverter( typeof( Sandbox.Internal.JsonConvert.Vector2IntConverter ) )]
[StructLayout( LayoutKind.Sequential )]
public struct Vector2Int : IEquatable<Vector2Int>, IParsable<Vector2Int>,
	System.Numerics.IAdditionOperators<Vector2Int, int, Vector2Int>,
	System.Numerics.IAdditionOperators<Vector2Int, float, Vector2>,
	System.Numerics.ISubtractionOperators<Vector2Int, int, Vector2Int>,
	System.Numerics.ISubtractionOperators<Vector2Int, float, Vector2>,
	System.Numerics.IMultiplyOperators<Vector2Int, int, Vector2Int>,
	System.Numerics.IMultiplyOperators<Vector2Int, float, Vector2>,
	System.Numerics.IDivisionOperators<Vector2Int, int, Vector2Int>,
	System.Numerics.IDivisionOperators<Vector2Int, float, Vector2>,

	System.Numerics.IAdditionOperators<Vector2Int, Vector2Int, Vector2Int>,
	System.Numerics.ISubtractionOperators<Vector2Int, Vector2Int, Vector2Int>,
	System.Numerics.IMultiplyOperators<Vector2Int, Vector2Int, Vector2Int>,
	System.Numerics.IDivisionOperators<Vector2Int, Vector2Int, Vector2Int>,

	System.Numerics.IAdditionOperators<Vector2Int, Vector2, Vector2>,
	System.Numerics.ISubtractionOperators<Vector2Int, Vector2, Vector2>,
	System.Numerics.IMultiplyOperators<Vector2Int, Vector2, Vector2>,
	System.Numerics.IDivisionOperators<Vector2Int, Vector2, Vector2>,

	System.Numerics.IAdditionOperators<Vector2Int, Vector3, Vector3>,
	System.Numerics.ISubtractionOperators<Vector2Int, Vector3, Vector3>,
	System.Numerics.IMultiplyOperators<Vector2Int, Vector3, Vector3>,
	System.Numerics.IDivisionOperators<Vector2Int, Vector3, Vector3>,

	System.Numerics.IAdditionOperators<Vector2Int, Vector4, Vector4>,
	System.Numerics.ISubtractionOperators<Vector2Int, Vector4, Vector4>,
	System.Numerics.IMultiplyOperators<Vector2Int, Vector4, Vector4>,
	System.Numerics.IDivisionOperators<Vector2Int, Vector4, Vector4>,

	System.Numerics.IUnaryNegationOperators<Vector2Int, Vector2Int>,

	System.Numerics.IAdditiveIdentity<Vector2Int, Vector2Int>,
	System.Numerics.IMultiplicativeIdentity<Vector2Int, Vector2Int>,
	System.Numerics.IEqualityOperators<Vector2Int, Vector2Int, bool>
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
	/// Initializes an integer vector with given components.
	/// </summary>
	/// <param name="x">The X component.</param>
	/// <param name="y">The Y component.</param>
	[ActionGraphNode( "vec2int.new" ), Title( "Vector2Int" ), Group( "Math/Geometry/Vector2Int" )]
	public Vector2Int( int x, int y )
	{
		this.x = x;
		this.y = y;
	}

	/// <summary>
	/// Initializes an integer vector with all components set to the same value.
	/// </summary>
	/// <param name="all">The value of the X and Y components.</param>
	public Vector2Int( int all = 0 ) : this( all, all )
	{
	}

	/// <summary>
	/// Initializes an integer vector with given components from another integer vector.
	/// </summary>
	public Vector2Int( Vector2Int vector2Int )
	{
		x = vector2Int.x;
		y = vector2Int.y;
	}

	/// <summary>
	/// Initializes an integer vector with given components from another integer vector, discarding the Z component.
	/// </summary>
	public Vector2Int( Vector3Int vector3Int )
	{
		x = vector3Int.x;
		y = vector3Int.y;
	}

	/// <summary>
	/// An integer vector with all components set to 1.
	/// </summary>
	public static readonly Vector2Int One = new( 1 );

	/// <summary>
	/// An integer vector with all components set to 0.
	/// </summary>
	public static readonly Vector2Int Zero = new( 0 );

	/// <summary>
	/// An integer vector with X set to 1. This represents the right direction.
	/// </summary>
	public static readonly Vector2Int Right = new( 1, 0 );

	/// <summary>
	/// An integer vector with X set to -1. This represents the left direction.
	/// </summary>
	public static readonly Vector2Int Left = new( -1, 0 );

	/// <summary>
	/// An integer vector with Y set to 1. This represents the up direction.
	/// </summary>
	public static readonly Vector2Int Up = new( 0, -1 );

	/// <summary>
	/// An integer vector with Y set to -1. This represents the down direction.
	/// </summary>
	public static readonly Vector2Int Down = new( 0, 1 );

	/// <summary>
	/// Formats the integer vector as a string "x,y".
	/// </summary>
	/// <returns></returns>
	public override string ToString()
	{
		return $"{x},{y}";
	}

	/// <summary>
	///  Returns a unit version of this vector. Keep in mind this returns a Vector2 and not a Vector2Int.
	/// </summary>
	public readonly Vector2 Normal
	{
		get
		{
			if ( IsZeroLength ) return this;

			return (Vector2)this / Length;
		}
	}

	/// <summary>
	/// Return the angle of this vector in degrees, always between 0 and 360.
	/// </summary>
	public readonly float Degrees => System.MathF.Atan2( x, -y ).RadianToDegree().NormalizeDegrees();


	// Vector2Int x Number Operators
	public static Vector2Int operator +( Vector2Int c1, int c2 )
	{
		return new Vector2Int( c1.x + c2, c1.y + c2 );
	}

	public static Vector2 operator +( Vector2Int c1, float c2 )
	{
		return new Vector2( c1.x + c2, c1.y + c2 );
	}
	public static Vector2Int operator -( Vector2Int c1, int c2 )
	{
		return new Vector2Int( c1.x - c2, c1.y - c2 );
	}

	public static Vector2 operator -( Vector2Int c1, float c2 )
	{
		return new Vector2( c1.x - c2, c1.y - c2 );
	}
	public static Vector2Int operator *( Vector2Int c1, int c2 )
	{
		return new Vector2Int( c1.x * c2, c1.y * c2 );
	}

	public static Vector2 operator *( Vector2Int c1, float c2 )
	{
		return new Vector2( c1.x * c2, c1.y * c2 );
	}
	public static Vector2Int operator /( Vector2Int c1, int c2 )
	{
		return new Vector2Int( c1.x / c2, c1.y / c2 );
	}

	public static Vector2 operator /( Vector2Int c1, float c2 )
	{
		return new Vector2( c1.x / c2, c1.y / c2 );
	}


	// Vector2Int x Vector2Int Operators
	public static Vector2Int operator +( Vector2Int c1, Vector2Int c2 )
	{
		return new Vector2Int( c1.x + c2.x, c1.y + c2.y );
	}
	public static Vector2Int operator -( Vector2Int c1, Vector2Int c2 )
	{
		return new Vector2Int( c1.x - c2.x, c1.y - c2.y );
	}
	public static Vector2Int operator /( Vector2Int c1, Vector2Int c2 )
	{
		return new Vector2Int( c1.x / c2.x, c1.y / c2.y );
	}
	public static Vector2Int operator *( Vector2Int c1, Vector2Int c2 )
	{
		return new Vector2Int( c1.x * c2.x, c1.y * c2.y );
	}

	// Vector2Int x Vector2 Operators
	public static Vector2 operator +( Vector2Int c1, Vector2 c2 )
	{
		return new Vector2( c1.x + c2.x, c1.y + c2.y );
	}
	public static Vector2 operator +( Vector2 c1, Vector2Int c2 )
	{
		return new Vector2( c1.x + c2.x, c1.y + c2.y );
	}
	public static Vector2 operator -( Vector2 c1, Vector2Int c2 )
	{
		return new Vector2( c1.x - c2.x, c1.y - c2.y );
	}
	public static Vector2 operator -( Vector2Int c1, Vector2 c2 )
	{
		return new Vector2( c1.x - c2.x, c1.y - c2.y );
	}
	public static Vector2 operator *( Vector2 c1, Vector2Int c2 )
	{
		return new Vector2( c1.x * c2.x, c1.y * c2.y );
	}
	public static Vector2 operator *( Vector2Int c1, Vector2 c2 )
	{
		return new Vector2( c1.x * c2.x, c1.y * c2.y );
	}
	public static Vector2 operator /( Vector2Int c1, Vector2 c2 )
	{
		return new Vector2( c1.x / c2.x, c1.y / c2.y );
	}

	public static Vector2 operator /( Vector2 c1, Vector2Int c2 )
	{
		return new Vector2( c1.x / c2.x, c1.y / c2.y );
	}

	// Vector2Int x Vector3 Operators
	public static Vector3 operator +( Vector2Int c1, Vector3 c2 )
	{
		return new Vector3( c1.x + c2.x, c1.y + c2.y, c2.z );
	}
	public static Vector3 operator +( Vector3 c1, Vector2Int c2 )
	{
		return new Vector3( c1.x + c2.x, c1.y + c2.y, c1.z );
	}
	public static Vector3 operator -( Vector2Int c1, Vector3 c2 )
	{
		return new Vector3( c1.x - c2.x, c1.y - c2.y, -c2.z );
	}
	public static Vector3 operator -( Vector3 c1, Vector2Int c2 )
	{
		return new Vector3( c1.x - c2.x, c1.y - c2.y, c1.z );
	}
	public static Vector3 operator *( Vector2Int c1, Vector3 c2 )
	{
		return new Vector3( c1.x * c2.x, c1.y * c2.y, c2.z );
	}
	public static Vector3 operator *( Vector3 c1, Vector2Int c2 )
	{
		return new Vector3( c1.x * c2.x, c1.y * c2.y, c1.z );
	}
	public static Vector3 operator /( Vector2Int c1, Vector3 c2 )
	{
		return new Vector3( c1.x / c2.x, c1.y / c2.y, c2.z );
	}
	public static Vector3 operator /( Vector3 c1, Vector2Int c2 )
	{
		return new Vector3( c1.x / c2.x, c1.y / c2.y, c1.z );
	}


	// Vector2Int x Vector4 Operators
	public static Vector4 operator +( Vector2Int c1, Vector4 c2 )
	{
		return new Vector4( c1.x + c2.x, c1.y + c2.y, c2.z, c2.w );
	}
	public static Vector4 operator +( Vector4 c1, Vector2Int c2 )
	{
		return new Vector4( c1.x + c2.x, c1.y + c2.y, c1.z, c1.w );
	}
	public static Vector4 operator -( Vector2Int c1, Vector4 c2 )
	{
		return new Vector4( c1.x - c2.x, c1.y - c2.y, -c2.z, -c2.w );
	}
	public static Vector4 operator -( Vector4 c1, Vector2Int c2 )
	{
		return new Vector4( c1.x - c2.x, c1.y - c2.y, c1.z, c1.w );
	}
	public static Vector4 operator *( Vector2Int c1, Vector4 c2 )
	{
		return new Vector4( c1.x * c2.x, c1.y * c2.y, c2.z, c2.w );
	}
	public static Vector4 operator *( Vector4 c1, Vector2Int c2 )
	{
		return new Vector4( c1.x * c2.x, c1.y * c2.y, c1.z, c1.w );
	}
	public static Vector4 operator /( Vector2Int c1, Vector4 c2 )
	{
		return new Vector4( c1.x / c2.x, c1.y / c2.y, c2.z, c2.w );
	}
	public static Vector4 operator /( Vector4 c1, Vector2Int c2 )
	{
		return new Vector4( c1.x / c2.x, c1.y / c2.y, c1.z, c1.w );
	}


	// Other Operators
	public static Vector2Int operator -( Vector2Int c1 )
	{
		return new Vector2Int( -c1.x, -c1.y );
	}


	// Implicit conversions
	static public implicit operator Vector2Int( int value )
	{
		return new Vector2Int( value, value );
	}
	static public implicit operator Vector2( Vector2Int value )
	{
		return new Vector2( value.x, value.y );
	}

	// Explicit conversions
	static public explicit operator Vector2Int( Vector2 value )
	{
		return new Vector2Int( (int)value.x, (int)value.y );
	}
	static public explicit operator Vector2Int( Vector3 value )
	{
		return new Vector2Int( (int)value.x, (int)value.y );
	}

	static public explicit operator Vector2Int( Vector4 value )
	{
		return new Vector2Int( (int)value.x, (int)value.y );
	}

	public int this[int index]
	{
		get
		{
			return index switch
			{
				0 => x,
				1 => y,
				_ => throw new IndexOutOfRangeException(),
			};
		}

		set
		{
			switch ( index )
			{
				case 0: x = value; break;
				case 1: y = value; break;
			}
		}
	}

	/// <summary>
	/// Length (or magnitude) of the integer vector (Distance from 0,0)
	/// </summary>
	public readonly float Length => MathF.Sqrt( x * x + y * y );

	/// <summary>
	/// Squared length of the integer vector. This is faster than <see cref="Length">Length</see>, and can be used for things like comparing distances, as long as only squared values are used."/>
	/// </summary>
	public readonly int LengthSquared => x * x + y * y;

	/// <summary>
	/// Returns an integer vector that runs perpendicular to this one.
	/// </summary>
	public readonly Vector2Int Perpendicular => new Vector2Int( -y, x );

	/// <summary>
	/// Whether the length of this vector is zero or not.
	/// </summary>
	public readonly bool IsZeroLength => x == 0 && y == 0;

	/// <summary>
	/// Returns true if value on every axis is less than or equal to tolerance
	/// </summary>
	public readonly bool IsNearlyZero( int tolerance = 0 )
	{
		return Math.Abs( x ) <= tolerance && Math.Abs( y ) <= tolerance;
	}

	public void Write( BinaryWriter writer )
	{
		writer.Write( x );
		writer.Write( y );
	}

	public readonly Vector2Int Read( BinaryReader reader )
	{
		return new Vector2Int( reader.ReadInt32(), reader.ReadInt32() );
	}

	/// <summary>
	/// Returns an integer vector that has the minimum values on each axis of the two input vectors.
	/// </summary>
	public readonly Vector2Int ComponentMin( Vector2Int other )
	{
		return new Vector2Int( Math.Min( x, other.x ), Math.Min( y, other.y ) );
	}

	/// <summary>
	/// Returns an integer vector that has the maximum values on each axis of the two input vectors.
	/// </summary>
	public readonly Vector2Int ComponentMax( Vector2Int other )
	{
		return new Vector2Int( Math.Max( x, other.x ), Math.Max( y, other.y ) );
	}

	/// <summary>
	/// Returns the distance between this vector and another.
	/// </summary>
	public float Distance( Vector2Int other )
	{
		var dx = other.x - x;
		var dy = other.y - y;
		return MathF.Sqrt( dx * dx + dy * dy );
	}

	/// <summary>
	/// Returns the distance between this vector and another.
	/// </summary>
	public float Distance( Vector2 other )
	{
		var dx = other.x - x;
		var dy = other.y - y;
		return MathF.Sqrt( dx * dx + dy * dy );
	}

	/// <summary>
	/// Snap to grid along any of the 2 axes.
	/// </summary>
	public readonly Vector2Int SnapToGrid( int gridSize, bool sx = true, bool sy = true )
	{
		return new Vector2Int(
			sx ? x.SnapToGrid( gridSize ) : x,
			sy ? y.SnapToGrid( gridSize ) : y
		);
	}

	/// <summary>
	/// Returns an integer vector that has the minimum values on each axis between 2 given vectors.
	/// </summary>
	public static Vector2Int Min( Vector2Int a, Vector2Int b ) => a.ComponentMin( b );

	/// <summary>
	/// Returns an integer vector that has the maximum values on each axis between 2 given vectors.
	/// </summary>
	public static Vector2Int Max( Vector2Int a, Vector2Int b ) => a.ComponentMax( b );

	/// <summary>
	/// Returns a new integer vector with all values positive. -5 becomes 5, ect.
	/// </summary>
	public readonly Vector2Int Abs()
	{
		return new Vector2Int( Math.Abs( x ), Math.Abs( y ) );
	}

	/// <summary>
	/// Returns this integer vector with given X component.
	/// </summary>
	public readonly Vector2Int WithX( int x ) => new( x, y );

	/// <summary>
	/// Returns this integer vector with given Y component.
	/// </summary>
	public readonly Vector2Int WithY( int y ) => new( x, y );

	#region Equality
	public static bool operator ==( Vector2Int left, Vector2Int right ) => left.Equals( right );
	public static bool operator !=( Vector2Int left, Vector2Int right ) => !(left == right);
	public override bool Equals( object obj ) => obj is Vector2Int o && Equals( o );
	public bool Equals( Vector2Int o ) => x == o.x && y == o.y;
	public override int GetHashCode() => HashCode.Combine( x, y );
	#endregion

	#region identity
	public static Vector2Int AdditiveIdentity => Zero;
	public static Vector2Int MultiplicativeIdentity => One;
	#endregion

	/// <summary>
	/// Given a string, try to convert this into a Vector2Int. Example formatting is "x,y", "[x,y]", "x y", etc.
	/// </summary>
	public static Vector2Int Parse( string str )
	{
		if ( TryParse( str, CultureInfo.InvariantCulture, out var res ) )
			return res;

		return default;
	}

	/// <inheritdoc cref="Parse(string)" />
	public static Vector2Int Parse( string str, IFormatProvider provider )
	{
		return Parse( str );
	}

	/// <inheritdoc cref="Parse(string)" />
	public static bool TryParse( [NotNullWhen( true )] string str, IFormatProvider info, [MaybeNullWhen( false )] out Vector2Int result )
	{
		result = Vector2Int.Zero;

		if ( string.IsNullOrWhiteSpace( str ) )
			return false;

		str = str.Trim( '[', ']', ' ', '\n', '\r', '\t', '"' );

		var components = str.Split( new[] { ' ', ',', ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries );

		if ( components.Length != 2 )
			return false;

		if ( !int.TryParse( components[0], NumberStyles.Integer, info, out int x ) ||
			!int.TryParse( components[1], NumberStyles.Integer, info, out int y ) )
		{
			return false;
		}

		result = new Vector2Int( x, y );
		return true;
	}
}
