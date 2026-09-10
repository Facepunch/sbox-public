namespace Sandbox.UI;

/// <summary>
/// The four corner radii of a box in pixels. Each corner has a horizontal (x) and vertical (y)
/// radius like CSS, so a corner is a quarter ellipse and a circle is the case where they match.
/// Build from a style with <see cref="FromStyle"/>, which resolves percentages and applies the
/// CSS overlap rule, then derive the padding box or shadow shape with <see cref="Inner"/> and
/// <see cref="Grow"/>. This is the only place radii should be resolved.
/// </summary>
internal struct BorderRadii
{
	public Vector2 TopLeft;
	public Vector2 TopRight;
	public Vector2 BottomLeft;
	public Vector2 BottomRight;

	public static readonly BorderRadii Zero = default;

	public readonly bool IsZero => TopLeft == Vector2.Zero && TopRight == Vector2.Zero && BottomLeft == Vector2.Zero && BottomRight == Vector2.Zero;

	/// <summary>
	/// Resolve a style's border radii against a border box. Percentages resolve against the box
	/// width for the horizontal radius and the height for the vertical one. Radii that would
	/// overlap along a side are all scaled down by the same factor, as CSS does, so corners keep
	/// their shape.
	/// </summary>
	public static BorderRadii FromStyle( Styles style, in Rect rect )
	{
		var r = new BorderRadii
		{
			TopLeft = Resolve( style.BorderTopLeftRadius, style.BorderTopLeftRadiusV, rect ),
			TopRight = Resolve( style.BorderTopRightRadius, style.BorderTopRightRadiusV, rect ),
			BottomLeft = Resolve( style.BorderBottomLeftRadius, style.BorderBottomLeftRadiusV, rect ),
			BottomRight = Resolve( style.BorderBottomRightRadius, style.BorderBottomRightRadiusV, rect ),
		};

		return r.Clamped( rect.Width, rect.Height );
	}

	// A corner with no vertical radius of its own is a circle
	static Vector2 Resolve( Length? horizontal, Length? vertical, in Rect rect )
	{
		if ( horizontal is not Length h ) return Vector2.Zero;

		var x = MathF.Max( 0, h.GetPixels( rect.Width ) );
		var y = MathF.Max( 0, (vertical ?? h).GetPixels( rect.Height ) );
		return new Vector2( x, y );
	}

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

		return new BorderRadii
		{
			TopLeft = TopLeft * f,
			TopRight = TopRight * f,
			BottomLeft = BottomLeft * f,
			BottomRight = BottomRight * f,
		};
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
}
