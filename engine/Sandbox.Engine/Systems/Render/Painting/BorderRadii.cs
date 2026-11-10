using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;

namespace Sandbox;

/// <summary>
/// The four corner radii of a box in pixels. Each corner has a horizontal (x) and vertical (y)
/// radius like CSS, so a corner is a quarter ellipse and a circle is the case where they match.
/// Clamp overlapping corners with <see cref="Clamped"/>, then derive the padding box or
/// shadow shape with <see cref="Inner"/> and <see cref="Grow"/>.
/// </summary>
internal struct BorderRadii
{
	public Vector2 TopLeft;
	public Vector2 TopRight;
	public Vector2 BottomLeft;
	public Vector2 BottomRight;

	public static readonly BorderRadii Zero = default;

	readonly Vector256<float> AsVector256() => Unsafe.BitCast<BorderRadii, Vector256<float>>( this );

	public readonly bool IsZero => Vector256.EqualsAll( AsVector256(), Vector256<float>.Zero );

	public readonly bool Equals( in BorderRadii other ) => Vector256.EqualsAll( AsVector256(), other.AsVector256() );

	/// <summary>
	/// The CSS overlap rule: if adjacent radii along any side sum to more than that side, every
	/// radius is scaled by the smallest ratio that makes them fit.
	/// </summary>
	public readonly BorderRadii Clamped( float width, float height )
	{
		var f = 1.0f;
		f = MinRatio( f, width, TopLeft.x + TopRight.x );
		f = MinRatio( f, width, BottomLeft.x + BottomRight.x );
		f = MinRatio( f, height, TopLeft.y + BottomLeft.y );
		f = MinRatio( f, height, TopRight.y + BottomRight.y );

		if ( f >= 1.0f ) return this;

		return Unsafe.BitCast<Vector256<float>, BorderRadii>( AsVector256() * f );
	}

	static float MinRatio( float f, float side, float sum )
	{
		if ( sum <= side || sum <= 0 ) return f;
		return MathF.Min( f, side / sum );
	}

	/// <summary>
	/// Radii of the padding box: each radius less the border on that side, floored at zero.
	/// A corner where either radius hits zero is square. Widths are left, top, right, bottom.
	/// </summary>
	public readonly BorderRadii Inner( in Vector4 borderWidth )
	{
		return new BorderRadii
		{
			TopLeft = Shrink( TopLeft, borderWidth.x, borderWidth.y ),
			TopRight = Shrink( TopRight, borderWidth.z, borderWidth.y ),
			BottomLeft = Shrink( BottomLeft, borderWidth.x, borderWidth.w ),
			BottomRight = Shrink( BottomRight, borderWidth.z, borderWidth.w ),
		};
	}

	static Vector2 Shrink( Vector2 r, float horizontal, float vertical )
	{
		var x = r.x - horizontal;
		var y = r.y - vertical;

		if ( x <= 0 || y <= 0 ) return Vector2.Zero;

		return new Vector2( x, y );
	}

	/// <summary>
	/// Radii of the box grown by a box-shadow spread. Positive grows, negative shrinks and floors
	/// at zero. Follows the CSS rule that eases small radii so a sharp corner stays sharp instead
	/// of suddenly rounding by the spread amount.
	/// </summary>
	public readonly BorderRadii Grow( float spread )
	{
		if ( spread == 0 ) return this;

		return new BorderRadii
		{
			TopLeft = Spread( TopLeft, spread ),
			TopRight = Spread( TopRight, spread ),
			BottomLeft = Spread( BottomLeft, spread ),
			BottomRight = Spread( BottomRight, spread ),
		};
	}

	static Vector2 Spread( Vector2 r, float spread )
	{
		return new Vector2( Spread( r.x, spread ), Spread( r.y, spread ) );
	}

	static float Spread( float r, float spread )
	{
		if ( spread < 0 )
			return MathF.Max( 0, r + spread );

		if ( r >= spread )
			return r + spread;

		// Corners smaller than the spread grow by less, so zero stays zero
		var t = r / spread - 1.0f;
		return r + spread * (1.0f + t * t * t);
	}

	/// <summary>
	/// Circular corners from the public API's Vector4, which is packed
	/// (bottom-right, top-right, bottom-left, top-left).
	/// </summary>
	public static BorderRadii FromPublic( in Vector4 v )
	{
		return new BorderRadii
		{
			BottomRight = new Vector2( v.x ),
			TopRight = new Vector2( v.y ),
			BottomLeft = new Vector2( v.z ),
			TopLeft = new Vector2( v.w ),
		};
	}

	/// <summary>
	/// Circular corners packed (top-left, top-right, bottom-left, bottom-right).
	/// </summary>
	public static BorderRadii FromCorners( in Vector4 v )
	{
		return new BorderRadii
		{
			TopLeft = new Vector2( v.x ),
			TopRight = new Vector2( v.y ),
			BottomLeft = new Vector2( v.z ),
			BottomRight = new Vector2( v.w ),
		};
	}

	/// <summary>
	/// The public API's Vector4: circle radii packed (bottom-right, top-right, bottom-left, top-left).
	/// </summary>
	public readonly Vector4 ToPublic()
	{
		return new Vector4( Circle( BottomRight ), Circle( TopRight ), Circle( BottomLeft ), Circle( TopLeft ) );
	}

	/// <summary>
	/// The circle radius each corner is drawn with by circle-only shaders:
	/// the smaller of the two radii, so the corner never overshoots either edge.
	/// </summary>
	readonly float Circle( in Vector2 r ) => MathF.Min( r.x, r.y );

	/// <summary>
	/// Circle radii packed as (top-left, top-right, bottom-left, bottom-right) - what the scissor,
	/// shadow and outline shaders take.
	/// </summary>
	public readonly Vector4 ToVector4()
	{
		return new Vector4( Circle( TopLeft ), Circle( TopRight ), Circle( BottomLeft ), Circle( BottomRight ) );
	}

	/// <summary>
	/// Horizontal radii packed as (top-left, top-right, bottom-left, bottom-right) - the order
	/// ui/rounded_rect.hlsl takes.
	/// </summary>
	public readonly Vector4 Horizontal => new( TopLeft.x, TopRight.x, BottomLeft.x, BottomRight.x );

	/// <summary>
	/// Vertical radii packed as (top-left, top-right, bottom-left, bottom-right).
	/// </summary>
	public readonly Vector4 Vertical => new( TopLeft.y, TopRight.y, BottomLeft.y, BottomRight.y );

	/// <summary>
	/// The largest radius on any corner, on either axis - how far in from the box's edge the rounding
	/// can reach.
	/// </summary>
	public readonly float Largest => MathF.Max(
		MathF.Max( MathF.Max( TopLeft.x, TopLeft.y ), MathF.Max( TopRight.x, TopRight.y ) ),
		MathF.Max( MathF.Max( BottomLeft.x, BottomLeft.y ), MathF.Max( BottomRight.x, BottomRight.y ) ) );
}
