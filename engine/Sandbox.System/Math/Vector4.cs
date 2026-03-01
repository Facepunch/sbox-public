using Sandbox;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;

/// <summary>
/// A 4-dimensional vector/point.
/// </summary>
[DataContract]
[JsonConverter( typeof( Sandbox.Internal.JsonConvert.Vector4Converter ) )]
[StructLayout( LayoutKind.Explicit, Pack = 16, Size = 16 )]
public struct Vector4 : System.IEquatable<Vector4>, IParsable<Vector4>
{
	[FieldOffset( 0 )]
	internal System.Numerics.Vector4 _vec;

	/// <summary>
	/// The X component of this Vector.
	/// </summary>
	[DataMember, ActionGraphInclude( AutoExpand = true )]
	public float x
	{
		[MethodImpl( MethodImplOptions.AggressiveInlining )]
		readonly get => _vec.X;
		[MethodImpl( MethodImplOptions.AggressiveInlining )]
		set => _vec.X = value;
	}

	/// <summary>
	/// The Y component of this Vector.
	/// </summary>
	[DataMember, ActionGraphInclude( AutoExpand = true )]
	public float y
	{
		[MethodImpl( MethodImplOptions.AggressiveInlining )]
		readonly get => _vec.Y;
		[MethodImpl( MethodImplOptions.AggressiveInlining )]
		set => _vec.Y = value;
	}

	/// <summary>
	/// The Z component of this Vector.
	/// </summary>
	[DataMember, ActionGraphInclude( AutoExpand = true )]
	public float z
	{
		[MethodImpl( MethodImplOptions.AggressiveInlining )]
		readonly get => _vec.Z;
		[MethodImpl( MethodImplOptions.AggressiveInlining )]
		set => _vec.Z = value;
	}

	/// <summary>
	/// The W component of this Vector.
	/// </summary>
	[DataMember, ActionGraphInclude( AutoExpand = true )]
	public float w
	{
		[MethodImpl( MethodImplOptions.AggressiveInlining )]
		readonly get => _vec.W;
		[MethodImpl( MethodImplOptions.AggressiveInlining )]
		set => _vec.W = value;
	}

	/// <summary>
	/// Initializes a vector4 with given components.
	/// </summary>
	/// <param name="x">The X component.</param>
	/// <param name="y">The Y component.</param>
	/// <param name="z">The Z component.</param>
	/// <param name="w">The W component.</param>
	[ActionGraphNode( "vec4.new" ), Title( "Vector4" ), Group( "Math/Geometry/Vector4" )]
	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public Vector4( float x, float y, float z, float w ) : this( new System.Numerics.Vector4( x, y, z, w ) )
	{
	}

	/// <summary>
	/// Initializes a 4D vector from a given Vector4, i.e. creating a copy.
	/// </summary>
	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public Vector4( in Vector4 other ) : this( other.x, other.y, other.z, other.w )
	{
	}

	/// <summary>
	/// Initializes a 4D vector from given #D vector and the given W component.
	/// </summary>
	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public Vector4( in Vector3 v, float w = 0.0f ) : this( new System.Numerics.Vector4( v._vec, w ) )
	{
	}

	/// <summary>
	/// Initializes the 4D vector with all components set to given value.
	/// </summary>
	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public Vector4( float all = 0.0f ) : this( all, all, all, all )
	{
	}

	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public Vector4( System.Numerics.Vector4 v )
	{
		_vec = v;
	}

	/// <summary>
	/// A 4D vector with all components set to 0.
	/// </summary>
	public static readonly Vector4 Zero = new Vector4( 0 );

	/// <summary>
	/// A 4D vector with all components set to 1.
	/// </summary>
	public static readonly Vector4 One = new Vector4( 1 );

	/// <summary>
	/// The length (or magnitude) of the vector (Distance from 0,0,0).
	/// </summary>
	[JsonIgnore]
	public readonly float Length => _vec.Length();

	/// <summary>
	/// Squared length of the vector. This is faster than <see cref="Length"/>, and can be used for things like comparing distances, as long as only squared values are used.
	/// </summary>
	[JsonIgnore]
	public readonly float LengthSquared => _vec.LengthSquared();

	/// <summary>
	/// Returns true if x, y, z or w are NaN
	/// </summary>
	[JsonIgnore]
	public readonly bool IsNaN => float.IsNaN( x ) || float.IsNaN( y ) || float.IsNaN( z ) || float.IsNaN( w );

	/// <summary>
	/// Returns true if x, y, z or w are infinity
	/// </summary>
	[JsonIgnore]
	public readonly bool IsInfinity => float.IsInfinity( x ) || float.IsInfinity( y ) || float.IsInfinity( z ) || float.IsInfinity( w );

	/// <summary>
	/// Whether length of this vector is nearly zero.
	/// </summary>
	[JsonIgnore]
	public readonly bool IsNearZeroLength => LengthSquared <= 1e-8;

	/// <summary>
	/// Returns this vector with given X component.
	/// </summary>
	/// <param name="x">The override for X component.</param>
	/// <returns>The new vector.</returns>
	public readonly Vector4 WithX( float x ) => new Vector4( x, y, z, w );

	/// <summary>
	/// Returns this vector with given Y component.
	/// </summary>
	/// <param name="y">The override for Y component.</param>
	/// <returns>The new vector.</returns>
	public readonly Vector4 WithY( float y ) => new Vector4( x, y, z, w );

	/// <summary>
	/// Returns this vector with given Z component.
	/// </summary>
	/// <param name="z">The override for Z component.</param>
	/// <returns>The new vector.</returns>
	public readonly Vector4 WithZ( float z ) => new Vector4( x, y, z, w );

	/// <summary>
	/// Returns this vector with given W component.
	/// </summary>
	/// <param name="w">The override for W component.</param>
	/// <returns>The new vector.</returns>
	public readonly Vector4 WithW( float w ) => new Vector4( x, y, z, w );

	#region operators
	public float this[int index]
	{
		[MethodImpl( MethodImplOptions.AggressiveInlining )]
		readonly get => _vec[index];
		[MethodImpl( MethodImplOptions.AggressiveInlining )]
		set => _vec[index] = value;
	}

	/// <summary>
	/// Returns true if value on every axis is less than tolerance away from zero
	/// </summary>
	public readonly bool IsNearlyZero( float tolerance = 0.0001f )
	{
		var abs = System.Numerics.Vector4.Abs( _vec );
		return abs.X < tolerance &&
			abs.Y < tolerance &&
			abs.Z < tolerance &&
			abs.W < tolerance;
	}

	/// <summary>
	/// Returns a vector each axis of which is clamped to between the 2 given vectors.
	/// </summary>
	/// <param name="otherMin">The mins vector. Values on each axis should be smaller than those of the maxs vector. See <see cref="Sort">Vector4.Sort</see>.</param>
	/// <param name="otherMax">The maxs vector. Values on each axis should be bigger than those of the mins vector. See <see cref="Sort">Vector4.Sort</see>.</param>
	public readonly Vector4 Clamp( Vector4 otherMin, Vector4 otherMax )
	{
		return System.Numerics.Vector4.Clamp( _vec, otherMin._vec, otherMax._vec );
	}

	/// <summary>
	/// Returns a vector each axis of which is clamped to given min and max values.
	/// </summary>
	/// <param name="min">Minimum value for each axis.</param>
	/// <param name="max">Maximum value for each axis.</param>
	public readonly Vector4 Clamp( float min, float max ) => Clamp( new Vector4( min ), new Vector4( max ) );

	/// <summary>
	/// Restricts a vector between a minimum and a maximum value.
	/// </summary>
	/// <param name="value">The vector to restrict.</param>
	/// <param name="min">The mins vector. Values on each axis should be smaller than those of the maxs vector. See <see cref="Sort">Vector4.Sort</see>.</param>
	/// <param name="max">The maxs vector. Values on each axis should be bigger than those of the mins vector. See <see cref="Sort">Vector4.Sort</see>.</param>
	public static Vector4 Clamp( in Vector4 value, in Vector4 min, in Vector4 max ) => System.Numerics.Vector4.Clamp( value._vec, min._vec, max._vec );

	/// <summary>
	/// Returns a vector that has the minimum values on each axis between this vector and given vector.
	/// </summary>
	public readonly Vector4 ComponentMin( in Vector4 other )
	{
		return System.Numerics.Vector4.Min( _vec, other._vec );
	}

	/// <summary>
	/// Returns a vector that has the minimum values on each axis between the 2 given vectors.
	/// </summary>
	public readonly Vector4 Min( in Vector4 a, in Vector4 b ) => a.ComponentMin( b );

	/// <summary>
	/// Returns a vector that has the maximum values on each axis between this vector and given vector.
	/// </summary>
	public readonly Vector4 ComponentMax( in Vector4 other )
	{
		return System.Numerics.Vector4.Max( _vec, other._vec );
	}

	/// <summary>
	/// Returns a vector that has the maximum values on each axis between the 2 given vectors.
	/// </summary>
	public static Vector4 Max( in Vector4 a, in Vector4 b ) => a.ComponentMax( b );

	/// <summary>
	/// Performs linear interpolation between 2 given vectors.
	/// </summary>
	/// <param name="a">Vector A</param>
	/// <param name="b">Vector B</param>
	/// <param name="frac">Fraction, where 0 would return Vector A, 0.5 would return a point between the 2 vectors, and 1 would return Vector B.</param>
	/// <param name="clamp">Whether to clamp the fraction argument between [0,1]</param>
	/// <returns></returns>
	[ActionGraphNode( "geom.lerp" ), Pure, Group( "Math/Geometry" ), Icon( "timeline" )]
	public static Vector4 Lerp( Vector4 a, Vector4 b, [Range( 0f, 1f )] float frac, bool clamp = true )
	{
		if ( clamp ) frac = frac.Clamp( 0, 1 );
		return new Vector4( a.x.LerpTo( b.x, frac ), a.y.LerpTo( b.y, frac ), a.z.LerpTo( b.z, frac ), a.w.LerpTo( b.w, frac ) );
	}

	/// <summary>
	/// Performs linear interpolation between this and given vectors.
	/// </summary>
	/// <param name="target">Vector B</param>
	/// <param name="frac">Fraction, where 0 would return Vector A, 0.5 would return a point between the 2 vectors, and 1 would return Vector B.</param>
	/// <param name="clamp">Whether to clamp the fraction argument between [0,1]</param>
	/// <returns></returns>
	public readonly Vector4 LerpTo( in Vector4 target, float frac, bool clamp = true ) => Lerp( this, target, frac, clamp );

	/// <summary>
	/// Performs linear interpolation between 2 given vectors, using a vector for the fraction on each axis.
	/// </summary>
	/// <param name="a">Vector A</param>
	/// <param name="b">Vector B</param>
	/// <param name="frac">Fraction for each axis, where 0 would return Vector A, 0.5 would return a point between the 2 vectors, and 1 would return Vector B.</param>
	/// <param name="clamp">Whether to clamp the fraction argument between [0,1] on each axis</param>
	/// <returns></returns>
	public static Vector4 Lerp( in Vector4 a, in Vector4 b, Vector4 frac, bool clamp = true )
	{
		if ( clamp ) frac = frac.Clamp( 0, 1 );
		return System.Numerics.Vector4.Lerp( a._vec, b._vec, frac._vec );
	}

	/// <summary>
	/// Performs linear interpolation between this and given vectors, with separate fraction for each vector component.
	/// </summary>
	/// <param name="target">Vector B</param>
	/// <param name="frac">Fraction for each axis, where 0 would return this, 0.5 would return a point between this and given vectors, and 1 would return the given vector.</param>
	/// <param name="clamp">Whether to clamp the fraction argument between [0,1] on each axis</param>
	/// <returns></returns>
	public readonly Vector4 LerpTo( in Vector4 target, in Vector4 frac, bool clamp = true ) => Lerp( this, target, frac, clamp );

	/// <summary>
	/// Returns the scalar/dot product of the 2 given vectors
	/// </summary>
	public static float Dot( in Vector4 a, in Vector4 b )
	{
		return System.Numerics.Vector4.Dot( a._vec, b._vec );
	}

	/// <summary>
	/// Returns the scalar/dot product of this vector and given vector.
	/// </summary>
	public readonly float Dot( in Vector4 b ) => Dot( this, b );

	[ActionGraphNode( "geom.distance" ), Pure, Title( "Distance" ), Group( "Math/Geometry" ), Icon( "straighten" )]
	public static float DistanceBetween( in Vector4 a, in Vector4 b )
	{
		return System.Numerics.Vector4.Distance( a._vec, b._vec );
	}

	/// <summary>
	/// Returns distance between this vector to given vector.
	/// </summary>
	public readonly float Distance( in Vector4 target ) => DistanceBetween( this, target );

	/// <summary>
	/// Returns squared distance between the 2 given vectors. This is faster than <see cref="DistanceBetween">DistanceBetween</see>,
	/// and can be used for things like comparing distances, as long as only squared values are used.
	/// </summary>
	public static float DistanceBetweenSquared( in Vector4 a, in Vector4 b )
	{
		return System.Numerics.Vector4.DistanceSquared( a._vec, b._vec );
	}

	/// <summary>
	/// Returns squared distance between this vector to given vector. This is faster than <see cref="Distance">Distance</see>,
	/// and can be used for things like comparing distances, as long as only squared values are used.
	/// </summary>
	public readonly float DistanceSquared( in Vector4 target ) => DistanceBetweenSquared( this, target );

	/// <summary>
	/// Sort these two vectors into min and max. This doesn't just swap the vectors, it sorts each component.
	/// So that min will come out containing the minimum x, y, z and w values.
	/// </summary>
	public static void Sort( ref Vector4 min, ref Vector4 max )
	{
		var a = new Vector4(
			Math.Min( min.x, max.x ),
			Math.Min( min.y, max.y ),
			Math.Min( min.z, max.z ),
			Math.Min( min.w, max.w ) );
		var b = new Vector4(
			Math.Max( min.x, max.x ),
			Math.Max( min.y, max.y ),
			Math.Max( min.z, max.z ),
			Math.Max( min.w, max.w ) );

		min = a;
		max = b;
	}

	/// <summary>
	/// Returns true if we're nearly equal to the passed vector.
	/// </summary>
	/// <param name="v">The value to compare with</param>
	/// <param name="delta">The max difference between component values</param>
	/// <returns>True if nearly equal</returns>
	public readonly bool AlmostEqual( in Vector4 v, float delta = 0.0001f )
	{
		if ( Math.Abs( x - v.x ) > delta ) return false;
		if ( Math.Abs( y - v.y ) > delta ) return false;
		if ( Math.Abs( z - v.z ) > delta ) return false;
		if ( Math.Abs( w - v.w ) > delta ) return false;

		return true;
	}

	/// <summary>
	/// Snap to grid along any of the 4 axes.
	/// </summary>
	public readonly Vector4 SnapToGrid( float gridSize, bool sx = true, bool sy = true, bool sz = true, bool sw = true )
	{
		return new Vector4(
			sx ? x.SnapToGrid( gridSize ) : x,
			sy ? y.SnapToGrid( gridSize ) : y,
			sz ? z.SnapToGrid( gridSize ) : z,
			sw ? w.SnapToGrid( gridSize ) : w );
	}


	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public static Vector4 operator +( in Vector4 c1, in Vector4 c2 ) => System.Numerics.Vector4.Add( c1._vec, c2._vec );

	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public static Vector4 operator -( in Vector4 c1, in Vector4 c2 ) => System.Numerics.Vector4.Subtract( c1._vec, c2._vec );

	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public static Vector4 operator *( in Vector4 c1, float f ) => System.Numerics.Vector4.Multiply( c1._vec, f );

	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public static Vector4 operator *( in Vector4 c1, in Vector4 c2 ) => System.Numerics.Vector4.Multiply( c1._vec, c2._vec );

	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public static Vector4 operator *( float f, in Vector4 c1 ) => System.Numerics.Vector4.Multiply( f, c1._vec );

	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public static Vector4 operator /( in Vector4 c1, float f ) => System.Numerics.Vector4.Divide( c1._vec, f );

	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public static Vector4 operator -( in Vector4 value ) => new Vector4( -value.x, -value.y, -value.z, -value.w );


	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	static public implicit operator Vector4( System.Numerics.Vector4 value ) => new Vector4( value.X, value.Y, value.Z, value.W );

	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	static public implicit operator Vector4( float value ) => new Vector4( value );

	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	static public implicit operator System.Numerics.Vector4( Vector4 value ) => new System.Numerics.Vector4( value.x, value.y, value.z, value.w );

	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public static implicit operator Vector4( in Color value ) => new Vector4( value.r, value.g, value.b, value.a );

	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public static implicit operator Vector4( Vector2 value ) => new Vector4( value.x, value.y, 0, 0 );


	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public static explicit operator Vector4( Vector3 value ) => new Vector4( value.x, value.y, value.z, 0 );

	#endregion

	#region equality
	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public static bool operator ==( Vector4 left, Vector4 right ) => left.Equals( right );
	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public static bool operator !=( Vector4 left, Vector4 right ) => !(left == right);
	public override readonly bool Equals( object obj ) => obj is Vector4 o && Equals( o );
	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public readonly bool Equals( Vector4 o ) => (_vec) == (o._vec);
	public readonly override int GetHashCode() => _vec.GetHashCode();
	#endregion


	/// <summary>
	/// Formats the Vector into a string "x,y,z,w"
	/// </summary>
	/// <returns></returns>
	public override string ToString()
	{
		return $"{x:0.###},{y:0.###},{z:0.###},{w:0.###}";
	}

	/// <summary>
	/// Given a string, try to convert this into a vector4. The format is "x,y,z,w".
	/// </summary>
	public static Vector4 Parse( string str )
	{
		if ( TryParse( str, CultureInfo.InvariantCulture, out var res ) )
			return res;

		return default;
	}

	/// <inheritdoc cref="Parse(string)" />
	public static Vector4 Parse( string str, IFormatProvider provider )
	{
		return Parse( str );
	}

	/// <inheritdoc cref="Parse(string)" />
	public static bool TryParse( string str, out Vector4 result )
	{
		return TryParse( str, CultureInfo.InvariantCulture, out result );
	}

	/// <inheritdoc cref="Parse(string)" />
	public static bool TryParse( [NotNullWhen( true )] string str, IFormatProvider provider, [MaybeNullWhen( false )] out Vector4 result )
	{
		result = Vector4.Zero;

		if ( string.IsNullOrWhiteSpace( str ) )
			return false;

		str = str.Trim( '[', ']', ' ', '\n', '\r', '\t', '"' );

		var components = str.Split( new[] { ' ', ',', ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries );

		if ( components.Length != 4 )
			return false;

		if ( !float.TryParse( components[0], NumberStyles.Float, provider, out float x ) ||
			!float.TryParse( components[1], NumberStyles.Float, provider, out float y ) ||
			!float.TryParse( components[2], NumberStyles.Float, provider, out float z ) ||
			!float.TryParse( components[3], NumberStyles.Float, provider, out float w ) )
		{
			return false;
		}

		result = new Vector4( x, y, z, w );
		return true;
	}
}
