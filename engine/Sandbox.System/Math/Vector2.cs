using Sandbox;
using Sandbox.Interpolation;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json.Serialization;

/// <summary>
/// A 2-dimensional vector. Typically represents a position, size, or direction in 2D space.
/// </summary>
[JsonConverter( typeof( Sandbox.Internal.JsonConvert.Vector2Converter ) )]
[StructLayout( LayoutKind.Sequential )]
public partial struct Vector2 : System.IEquatable<Vector2>, IParsable<Vector2>, IInterpolator<Vector2>
{
	internal System.Numerics.Vector2 _vec;

	/// <summary>
	/// X component of this vector.
	/// </summary>
	[ActionGraphInclude( AutoExpand = true )]
	public float x
	{
		[MethodImpl( MethodImplOptions.AggressiveInlining )]
		readonly get => _vec.X;
		[MethodImpl( MethodImplOptions.AggressiveInlining )]
		set => _vec.X = value;
	}

	/// <summary>
	/// Y component of this vector.
	/// </summary>
	[ActionGraphInclude( AutoExpand = true )]
	public float y
	{
		[MethodImpl( MethodImplOptions.AggressiveInlining )]
		readonly get => _vec.Y;
		[MethodImpl( MethodImplOptions.AggressiveInlining )]
		set => _vec.Y = value;
	}

	/// <summary>
	/// Initializes a 2D vector with given components.
	/// </summary>
	/// <param name="x">The X component.</param>
	/// <param name="y">The Y component.</param>
	[ActionGraphNode( "vec2.new" ), Title( "Vector2" ), Group( "Math/Geometry/Vector2" )]
	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public Vector2( float x, float y ) : this( new System.Numerics.Vector2( x, y ) )
	{
	}

	/// <summary>
	/// Initializes a Vector2 from a given Vector2, i.e. creating a copy.
	/// </summary>
	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public Vector2( in Vector2 other ) : this( other.x, other.y )
	{
	}

	/// <summary>
	/// Initializes the 2D vector with all components set to the given value.
	/// </summary>
	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public Vector2( float all ) : this( all, all )
	{
	}

	/// <summary>
	/// Initializes the 2D vector with components from given 3D Vector, discarding the Z component.
	/// </summary>
	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public Vector2( Vector3 v ) : this( new System.Numerics.Vector2( (float)v.x, (float)v.y ) )
	{
	}

	/// <summary>
	/// Initializes the 2D vector with components from given 4D vector, discarding the Z and W components.
	/// </summary>
	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public Vector2( Vector4 v ) : this( new System.Numerics.Vector2( (float)v.x, (float)v.y ) )
	{
	}

	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public Vector2( System.Numerics.Vector2 v )
	{
		_vec = v;
	}


	/// <summary>
	/// Returns a 2D vector with every component set to 1
	/// </summary>
	public static Vector2 One { get; } = new Vector2( 1 );

	/// <summary>
	/// Returns a 2D vector with every component set to 0
	/// </summary>
	public static Vector2 Zero { get; } = new Vector2( 0 );

	/// <summary>
	/// Returns a 2D vector with Y set to -1. This typically represents up in 2D space.
	/// </summary>
	public static Vector2 Up { get; } = new Vector2( 0, -1 );

	/// <summary>
	/// Returns a 2D vector with Y set to 1. This typically represents down in 2D space.
	/// </summary>
	public static Vector2 Down { get; } = new Vector2( 0, 1 );

	/// <summary>
	/// Returns a 2D vector with X set to -1. This typically represents the left hand direction in 2D space.
	/// </summary>
	public static Vector2 Left { get; } = new Vector2( -1, 0 );

	/// <summary>
	/// Returns a 2D vector with X set to 1. This typically represents the right hand direction in 2D space.
	/// </summary>
	public static Vector2 Right { get; } = new Vector2( 1, 0 );

	/// <summary>
	/// Uniformly samples a 2D position from all points with distance at most 1 from the origin.
	/// </summary>
	[ActionGraphNode( "vec2.random" ), Title( "Random Vector2" ), Group( "Math/Geometry/Vector2" ), Icon( "casino" )]
	public static Vector2 Random => SandboxSystem.Random.VectorInCircle();

	/// <summary>
	/// Returns a point on a circle at given rotation from X axis, counter clockwise.
	/// </summary>
	[ActionGraphNode( "vec2.fromrads" ), Pure, Title( "Vector2 From Radians" ), Group( "Math/Geometry/Vector2" ), Icon( "architecture" )]
	public static Vector2 FromRadians( float radians ) => new Vector2( MathF.Sin( radians ), -MathF.Cos( radians ) );

	/// <summary>
	/// Returns a point on a circle at given rotation from X axis, counter clockwise.
	/// </summary>
	[ActionGraphNode( "vec2.fromdegs" ), Pure, Title( "Vector2 From Degrees" ), Group( "Math/Geometry/Vector2" ), Icon( "architecture" )]
	public static Vector2 FromDegrees( float degrees ) => FromRadians( degrees.DegreeToRadian() );


	/// <summary>
	/// Return the same vector but with a length of one
	/// </summary>
	[JsonIgnore]
	public readonly Vector2 Normal => IsNearZeroLength ? Vector2.Zero : System.Numerics.Vector2.Normalize( _vec );

	/// <summary>
	/// Returns the magnitude of the vector
	/// </summary>
	[JsonIgnore]
	public readonly float Length => _vec.Length();

	/// <summary>
	/// This is faster than Length, so is better to use in certain circumstances
	/// </summary>
	[JsonIgnore]
	public readonly float LengthSquared => _vec.LengthSquared();


	/// <summary>
	/// Returns the inverse of this vector, which is useful for scaling vectors
	/// </summary>
	[JsonIgnore]
	public readonly Vector2 Inverse => new( 1.0f / x, 1.0f / y );

	/// <summary>
	/// Return the angle of this vector in degrees, always between 0 and 360
	/// </summary>
	[JsonIgnore]
	public readonly float Degrees => System.MathF.Atan2( x, -y ).RadianToDegree().NormalizeDegrees();

	/// <summary>
	/// Returns a vector that runs perpendicular to this one
	/// </summary>
	[JsonIgnore]
	public readonly Vector2 Perpendicular => new Vector2( -y, x );

	/// <summary>
	/// Returns true if x, y, or z are NaN
	/// </summary>
	[JsonIgnore]
	public readonly bool IsNaN => float.IsNaN( x ) || float.IsNaN( y );

	/// <summary>
	/// Returns true if x, y, or z are infinity
	/// </summary>
	[JsonIgnore]
	public readonly bool IsInfinity => float.IsInfinity( x ) || float.IsInfinity( y );

	/// <summary>
	/// Returns true if the squared length is less than 1e-8 (which is really near zero)
	/// </summary>
	[JsonIgnore]
	public readonly bool IsNearZeroLength => LengthSquared <= 1e-8;

	/// <summary>
	/// Return this vector with given X.
	/// </summary>
	public readonly Vector2 WithX( float x ) => new Vector2( x, y );

	/// <summary>
	/// Return this vector with given Y.
	/// </summary>
	public readonly Vector2 WithY( float y ) => new Vector2( x, y );

	/// <summary>
	/// Returns true if value on every axis is less than tolerance away from zero
	/// </summary>
	public readonly bool IsNearlyZero( float tolerance = 0.0001f )
	{
		var abs = System.Numerics.Vector2.Abs( _vec );
		return abs.X < tolerance && abs.Y < tolerance;
	}

	/// <summary>
	/// Returns a vector whose length is limited to given maximum
	/// </summary>
	public readonly Vector2 ClampLength( float maxLength )
	{
		if ( LengthSquared <= 0 )
			return Zero;

		if ( LengthSquared < (maxLength * maxLength) )
			return this;

		return Normal * maxLength;
	}

	/// <summary>
	/// Returns a vector whose length is limited between given minimum and maximum
	/// </summary>
	public readonly Vector2 ClampLength( float minLength, float maxLength )
	{
		float minSqr = minLength * minLength;
		float maxSqr = maxLength * maxLength;
		float lenSqr = LengthSquared;

		if ( lenSqr <= 0.0f )
			return Zero;

		if ( lenSqr <= minSqr )
			return Normal * minLength;
		if ( lenSqr >= maxSqr )
			return Normal * maxLength;

		return this;
	}

	/// <summary>
	/// Returns a vector each axis of which is clamped to between the 2 given vectors. Basically clamps a point to a square.
	/// </summary>
	/// <param name="otherMin">The mins vector. Values on each axis should be smaller than those of the maxs vector. See <see cref="Sort">Vector2.Sort</see>.</param>
	/// <param name="otherMax">The maxs vector. Values on each axis should be larger than those of the mins vector. See <see cref="Sort">Vector2.Sort</see>.</param>
	public readonly Vector2 Clamp( Vector2 otherMin, Vector2 otherMax )
	{
		return new Vector2( Math.Clamp( x, otherMin.x, otherMax.x ), Math.Clamp( y, otherMin.y, otherMax.y ) );
	}

	/// <summary>
	/// Returns a vector each axis of which is clamped to given min and max values.
	/// </summary>
	/// <param name="min">Minimum value for each axis.</param>
	/// <param name="max">Maximum value for each axis.</param>
	public readonly Vector2 Clamp( float min, float max ) => Clamp( new Vector2( min ), new Vector2( max ) );

	/// <summary>
	/// Restricts a vector between a minimum and maximum value.
	/// </summary>
	/// <param name="value">The vector to restrict.</param>
	/// <param name="min">The mins vector. Values on each axis should be smaller than those of the maxs vector. See <see cref="Sort">Vector2.Sort</see>.</param>
	/// <param name="max">The maxs vector. Values on each axis should be larger than those of the mins vector. See <see cref="Sort">Vector2.Sort</see>.</param>
	/// <returns></returns>
	public static Vector2 Clamp( in Vector2 value, in Vector2 min, in Vector2 max ) => System.Numerics.Vector2.Clamp( value, min, max );

	/// <summary>
	/// Returns a vector that has the minimum values on each axis between this vector and given vector.
	/// </summary>
	public readonly Vector2 ComponentMin( Vector2 other )
	{
		return System.Numerics.Vector2.Min( _vec, other._vec );
	}

	/// <summary>
	/// Returns a vector that has the minimum values on each axis between the 2 given vectors.
	/// </summary>
	public static Vector2 Min( Vector2 a, Vector2 b ) => a.ComponentMin( b );

	/// <summary>
	/// Returns a vector that has the maximum values on each axis between this vector and given vector.
	/// </summary>
	public readonly Vector2 ComponentMax( Vector2 other )
	{
		return System.Numerics.Vector2.Max( _vec, other._vec );
	}

	/// <summary>
	/// Returns a vector that has the maximum values on each axis between the 2 given vectors.
	/// </summary>
	public static Vector2 Max( Vector2 a, Vector2 b ) => a.ComponentMax( b );

	/// <summary>
	/// Linearly interpolate from point a to point b.
	/// </summary>
	[ActionGraphNode( "geom.lerp" ), Pure, Group( "Math/Geometry" ), Icon( "timeline" )]
	public static Vector2 Lerp( Vector2 a, Vector2 b, [Range( 0f, 1f )] float frac, bool clamp = true )
	{
		if ( clamp ) frac = frac.Clamp( 0.0f, 1.0f );
		return System.Numerics.Vector2.Lerp( a._vec, b._vec, frac );
	}

	/// <summary>
	/// Linearly interpolate from this vector to given vector.
	/// </summary>
	public readonly Vector2 LerpTo( Vector2 target, float t, bool clamp = true )
	{
		return Lerp( this, target, t, clamp );
	}

	/// <summary>
	/// Linearly interpolate from point a to point b with separate fraction for each axis.
	/// </summary>
	public static Vector2 Lerp( Vector2 a, Vector2 b, Vector2 t, bool clamp = true )
	{
		if ( clamp ) t = t.Clamp( 0.0f, 1.0f );
		return System.Numerics.Vector2.Lerp( a._vec, b._vec, t );
	}

	/// <summary>
	/// Linearly interpolate from this vector to given vector with separate fraction for each axis.
	/// </summary>
	public readonly Vector2 LerpTo( Vector2 target, Vector2 t, bool clamp = true )
	{
		return Lerp( this, target, t, clamp );
	}

	/// <summary>
	/// Returns the scalar/dot product between the 2 given vectors.
	/// </summary>
	[ActionGraphNode( "geom.dot" ), Pure, Group( "Math/Geometry" ), Icon( "fiber_manual_record" )]
	public static float Dot( Vector2 a, Vector2 b )
	{
		return System.Numerics.Vector2.Dot( a._vec, b._vec );
	}

	/// <summary>
	/// Returns the scalar/dot product between this and the given vector.
	/// </summary>
	public readonly float Dot( in Vector2 b ) => Dot( this, b );


	/// <summary>
	/// Returns distance between the 2 given vectors.
	/// </summary>
	public static float DistanceBetween( Vector2 a, Vector2 b ) => System.Numerics.Vector2.Distance( a._vec, b._vec );

	/// <summary>
	/// Returns distance between the 2 given vectors.
	/// </summary>
	[ActionGraphNode( "geom.dist" ), Pure, Title( "Distance" ), Group( "Math/Geometry" ), Icon( "straighten" )]
	public static float Distance( in Vector2 a, in Vector2 b ) => System.Numerics.Vector2.Distance( a._vec, b._vec );

	/// <summary>
	/// Returns distance between this and given vectors.
	/// </summary>
	public readonly float Distance( Vector2 target ) => DistanceBetween( this, target );

	/// <summary>
	/// Returns squared distance between the 2 given vectors. This is faster than <see cref="DistanceBetween">DistanceBetween</see>,
	/// and can be used for things like comparing distances, as long as only squared values are used.
	/// </summary>
	public static float DistanceBetweenSquared( Vector2 a, Vector2 b )
	{
		return (b - a).LengthSquared;
	}

	/// <summary>
	/// Returns squared distance between the 2 given vectors. This is faster than <see cref="DistanceBetween">DistanceBetween</see>,
	/// and can be used for things like comparing distances, as long as only squared values are used.
	/// </summary>
	public static float DistanceSquared( in Vector2 a, in Vector2 b ) => System.Numerics.Vector2.DistanceSquared( a._vec, b._vec );

	/// <summary>
	/// Returns squared distance between the 2 given vectors. This is faster than <see cref="Distance(Vector2)">Distance</see>,
	/// and can be used for things like comparing distances, as long as only squared values are used.
	/// </summary>
	public readonly float DistanceSquared( Vector2 target ) => DistanceBetweenSquared( this, target );

	/// <summary>
	/// Calculates the normalized direction vector from one point to another in 2D space.
	/// </summary>
	public static Vector2 Direction( in Vector2 from, in Vector2 to )
	{
		return (to - from).Normal;
	}

	/// <summary>
	/// Given a vector like 1,1,1 and direction 1,0,0, will return 0,1,1.
	/// This is useful for velocity collision type events, where you want to
	/// cancel out velocity based on a normal.
	/// For this to work properly, direction should be a normal, but you can scale
	/// how much you want to subtract by scaling the direction. Ie, passing in a direction
	/// with a length of 0.5 will remove half the direction.
	/// </summary>
	public readonly Vector2 SubtractDirection( in Vector2 direction, float strength = 1.0f )
	{
		return this - (direction * Dot( direction ) * strength);
	}


	/// <summary>
	/// Returns a new vector whos length is closer to given target length by given amount.
	/// </summary>
	public readonly Vector2 Approach( float length, float amount )
	{
		return Normal * Length.Approach( length, amount );
	}

	/// <summary>
	/// Returns a new vector with all values positive. -5 becomes 5, etc.
	/// </summary>
	public readonly Vector2 Abs()
	{
		return System.Numerics.Vector2.Abs( _vec );
	}

	/// <summary>
	/// Returns a new vector with all values positive. -5 becomes 5, etc.
	/// </summary>
	public static Vector2 Abs( in Vector2 value )
	{
		return System.Numerics.Vector2.Abs( value._vec );
	}

	/// <summary>
	/// Returns a reflected vector based on incoming direction and plane normal. Like a ray reflecting off of a mirror.
	/// </summary>
	public static Vector2 Reflect( in Vector2 direction, in Vector2 normal )
	{
		return System.Numerics.Vector2.Reflect( direction._vec, normal._vec );
	}

	/// <summary>
	/// Sort these two vectors into min and max. This doesn't just swap the vectors, it sorts each component.
	/// So that min will come out containing the minimum x and y values.
	/// </summary>
	public static void Sort( ref Vector2 min, ref Vector2 max )
	{
		var a = new Vector2( Math.Min( min.x, max.x ), Math.Min( min.y, max.y ) );
		var b = new Vector2( Math.Max( min.x, max.x ), Math.Max( min.y, max.y ) );

		min = a;
		max = b;
	}

	/// <summary>
	/// Returns true if we're nearly equal to the passed vector.
	/// </summary>
	/// <param name="v">The value to compare with</param>
	/// <param name="delta">The max difference between component values</param>
	/// <returns>True if nearly equal</returns>
	public readonly bool AlmostEqual( Vector2 v, float delta = 0.0001f )
	{
		if ( Math.Abs( x - v.x ) > delta ) return false;
		if ( Math.Abs( y - v.y ) > delta ) return false;

		return true;
	}

	/// <summary>
	/// Calculates position of a point on a cubic bezier curve at given fraction.
	/// </summary>
	/// <param name="source">Point A of the curve.</param>
	/// <param name="target">Point B of the curve.</param>
	/// <param name="sourceTangent">Tangent for the Point A.</param>
	/// <param name="targetTangent">Tangent for the Point B.</param>
	/// <param name="t">How far along the path to get a point on. Range is 0 to 1, inclusive.</param>
	/// <returns>The point on the curve</returns>
	public static Vector2 CubicBezier( in Vector2 source, in Vector2 target, in Vector2 sourceTangent, in Vector2 targetTangent, float t )
	{
		t = t.Clamp( 0, 1 );

		var invT = 1 - t;
		return invT * invT * invT * source +
			3 * invT * invT * t * sourceTangent +
			3 * invT * t * t * targetTangent +
			t * t * t * target;
	}

	/// <summary>
	/// Snap to grid along all 3 axes.
	/// </summary>
	public readonly Vector2 SnapToGrid( float gridSize, bool sx = true, bool sy = true )
	{
		return new Vector2( sx ? x.SnapToGrid( gridSize ) : x, sy ? y.SnapToGrid( gridSize ) : y );
	}

	/// <summary>
	/// Returns the distance between two direction vectors in degrees.
	/// </summary>
	public static float GetAngle( in Vector2 v1, in Vector2 v2 )
	{
		return MathF.Acos( Dot( v1.Normal, v2.Normal ).Clamp( -1, 1 ) ).RadianToDegree();
	}

	/// <summary>
	/// Returns the distance between this vector and another in degrees.
	/// </summary>
	public readonly float Angle( in Vector2 other )
	{
		return GetAngle( this, other );
	}

	/// <summary>
	/// Try to add to this vector. If we're already over max then don't add.
	/// If we're over max when we add, clamp in that direction so we're not.
	/// </summary>
	public readonly Vector2 AddClamped( in Vector2 toAdd, float maxLength )
	{
		var dir = toAdd.Normal;

		// Already over - just return self
		var dot = Dot( dir );
		if ( dot > maxLength ) return this;

		// Add it
		var vec = this + toAdd;
		dot = vec.Dot( dir );
		if ( dot < maxLength ) return vec;

		// We're over, take off the rest
		vec -= dir * (dot - maxLength);
		return vec;
	}

	/// <summary>
	/// Rotate this vector around given point by given angle in degrees and
	/// return the result as a new vector.
	/// </summary>
	/// <param name="center"></param>
	/// <param name="angleDegrees"></param>
	/// <returns></returns>
	public readonly Vector2 RotateAround( in Vector2 center, float angleDegrees )
	{
		var radians = angleDegrees.DegreeToRadian();
		var cos = MathF.Cos( radians );
		var sin = MathF.Sin( radians );
		var dx = x - center.x;
		var dy = y - center.y;
		return new Vector2(
			center.x + (dx * cos - dy * sin),
			center.y + (dx * sin + dy * cos) );
	}

	#region operators
	public float this[int index]
	{
		[MethodImpl( MethodImplOptions.AggressiveInlining )]
		readonly get => _vec[index];
		[MethodImpl( MethodImplOptions.AggressiveInlining )]
		set => _vec[index] = value;
	}

	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public static Vector2 operator +( Vector2 c1, Vector2 c2 ) => c1._vec + c2._vec;
	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public static Vector2 operator -( Vector2 c1, Vector2 c2 ) => c1._vec - c2._vec;
	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public static Vector2 operator -( Vector2 c1 ) => System.Numerics.Vector2.Negate( c1._vec );
	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public static Vector2 operator *( Vector2 c1, float f ) => c1._vec * f;
	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public static Vector2 operator *( float f, Vector2 c1 ) => c1._vec * f;
	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public static Vector2 operator *( Vector2 c1, Vector2 c2 ) => c1._vec * c2._vec;
	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public static Vector2 operator /( Vector2 c1, Vector2 c2 ) => c1._vec / c2._vec;
	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public static Vector2 operator /( Vector2 c1, float c2 ) => c1._vec / c2;

	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public static implicit operator Vector2( System.Numerics.Vector2 value ) => new Vector2( value );
	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public static implicit operator System.Numerics.Vector2( Vector2 value ) => new System.Numerics.Vector2( value.x, value.y );
	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public static implicit operator Vector2( double value ) => new Vector2( (float)value, (float)value );
	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public static implicit operator Vector2( Vector3 value ) => new Vector2( value );
	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public static implicit operator Vector2( Vector4 value ) => new Vector2( value );
	#endregion

	#region equality
	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public static bool operator ==( Vector2 left, Vector2 right ) => left.Equals( right );
	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public static bool operator !=( Vector2 left, Vector2 right ) => !(left == right);
	public override readonly bool Equals( object obj ) => obj is Vector2 o && Equals( o );
	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public readonly bool Equals( Vector2 o ) => (_vec) == (o._vec);
	public readonly override int GetHashCode() => _vec.GetHashCode();
	#endregion

	/// <summary>
	/// Formats the vector into a string "x,y"
	/// </summary>
	public override readonly string ToString()
	{
		var _x = x;
		var _y = y;

		// avoid -0
		if ( _x.AlmostEqual( 0 ) ) _x = 0.0f;
		if ( _y.AlmostEqual( 0 ) ) _y = 0.0f;

		return $"{_x:0.###},{_y:0.###}";
	}

	/// <summary>
	/// Given a string, try to convert this into a Vector2. Example formatting is "x,y", "[x,y]", "x y", etc.
	/// </summary>
	public static Vector2 Parse( string str )
	{
		if ( TryParse( str, CultureInfo.InvariantCulture, out var res ) )
			return res;

		return default;
	}

	/// <inheritdoc cref="Parse(string)" />
	public static bool TryParse( string str, out Vector2 result )
	{
		return TryParse( str, CultureInfo.InvariantCulture, out result );
	}

	/// <inheritdoc cref="Parse(string)" />
	public static Vector2 Parse( string str, IFormatProvider provider )
	{
		return Parse( str );
	}

	/// <inheritdoc cref="Parse(string)" />
	public static bool TryParse( [NotNullWhen( true )] string str, IFormatProvider provider, [MaybeNullWhen( false )] out Vector2 result )
	{
		result = Vector2.Zero;

		if ( string.IsNullOrWhiteSpace( str ) )
			return false;

		str = str.Trim( '[', ']', ' ', '\n', '\r', '\t', '"' );

		var components = str.Split( new[] { ' ', ',', ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries );

		if ( components.Length != 2 )
			return false;

		if ( !float.TryParse( components[0], NumberStyles.Float, provider, out float x ) ||
			!float.TryParse( components[1], NumberStyles.Float, provider, out float y ) )
		{
			return false;
		}

		result = new Vector2( x, y );
		return true;
	}

	/// <summary>
	/// Move to the target vector, by amount acceleration
	/// </summary>
	public readonly Vector2 WithAcceleration( Vector2 target, float accelerate )
	{
		if ( target.IsNearZeroLength )
			return this;

		Vector2 wishDir = target.Normal;
		float wishSpeed = target.Length;

		// See if we are changing direction a bit
		var currentSpeed = Dot( wishDir );

		// Reduce wishspeed by the amount of veer
		var addSpeed = wishSpeed - currentSpeed;

		// If not going to add any speed, done.
		if ( addSpeed <= 0.0f )
			return this;

		// Determine amount of acceleration
		var accelSpeed = accelerate * wishSpeed;

		// Cap at addSpeed
		if ( accelSpeed > addSpeed )
			accelSpeed = addSpeed;

		return this + wishDir * accelSpeed;
	}

	public readonly Vector2 WithFriction( float frictionAmount, float stopSpeed = 140.0f )
	{
		var speed = Length;
		if ( speed < 0.01f ) return this;

		// Bleed off some speed, but if we have less than the bleed
		// threshold, bleed the threshold amount
		float control = (speed < stopSpeed) ? stopSpeed : speed;

		// Add the amount to the drop amount
		var drop = control * frictionAmount;

		// Scale the velocity
		float newSpeed = speed - drop;
		if ( newSpeed < 0 ) newSpeed = 0;
		if ( newSpeed == speed ) return this;

		newSpeed /= speed;
		return this * newSpeed;
	}

	Vector2 IInterpolator<Vector2>.Interpolate( Vector2 a, Vector2 b, float delta )
	{
		return a.LerpTo( b, delta );
	}
}
