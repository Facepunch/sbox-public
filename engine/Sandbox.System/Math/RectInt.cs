using Sandbox.UI;
using System.Runtime.InteropServices;
using System.Text.Json.Serialization;

namespace Sandbox;

#pragma warning disable CS0618 // Type or member is obsolete
/// <summary>
/// Represents a rectangle but with whole numbers
/// </summary>
[StructLayout( LayoutKind.Sequential )]
[Description( "A rectangle with an integer position and size in 2D space." )]
public struct RectInt : System.IEquatable<RectInt>
{
	private int left;
	private int top;
	private int right;
	private int bottom;

	/// <summary>
	/// Initialize a Rect at given position and with given size.
	/// </summary>
	public RectInt( in int x, in int y, in int width, in int height )
	{
		left = x;
		top = y;
		right = x + width;
		bottom = y + height;
	}

	/// <summary>
	/// Initialize a Rect at given position and with given size.
	/// </summary>
	public RectInt( in Vector2Int point, in Vector2Int size = default )
	{
		left = point.x;
		top = point.y;
		right = point.x + size.x;
		bottom = point.y + size.y;
	}

	/// <summary>
	/// Width of the rect.
	/// </summary>
	[JsonIgnore, Hide]
	public int Width
	{
		readonly get => right - left;
		set => right = left + value;
	}

	/// <summary>
	/// Height of the rect.
	/// </summary>
	[JsonIgnore, Hide]
	public int Height
	{
		readonly get => bottom - top;
		set => bottom = top + value;
	}

	/// <summary>
	/// Position of rect's left edge relative to its parent, can also be interpreted as its position on the X axis.
	/// </summary>
	[JsonIgnore, Hide]
	public int Left
	{
		readonly get => left;
		set => left = value;
	}

	/// <summary>
	/// Position of rect's top edge relative to its parent, can also be interpreted as its position on the Y axis.
	/// </summary>
	[JsonIgnore, Hide]
	public int Top
	{
		readonly get => top;
		set => top = value;
	}

	/// <summary>
	/// Position of rect's right edge relative to its parent.
	/// </summary>
	[JsonIgnore, Hide]
	public int Right
	{
		readonly get => right;
		set => right = value;
	}

	/// <summary>
	/// Position of rect's bottom edge relative to its parent.
	/// </summary>
	[JsonIgnore, Hide]
	public int Bottom
	{
		readonly get => bottom;
		set => bottom = value;
	}

	/// <summary>
	/// Position of this rect.
	/// </summary>
	public Vector2Int Position
	{
		readonly get => new( left, top );
		set
		{
			var s = Size;
			left = value.x;
			top = value.y;
			Size = s;
		}
	}

	/// <summary>
	/// Center of this rect.
	/// </summary>
	[JsonIgnore, Hide]
	public readonly Vector2 Center
	{
		get => Position + Size * 0.5f;
	}

	/// <summary>
	/// Size of this rect.
	/// </summary>
	public Vector2Int Size
	{
		readonly get => new( Width, Height );
		set
		{
			right = left + value.x;
			bottom = top + value.y;
		}
	}

	/// <summary>
	/// Returns this rect with position set to 0 on both axes.
	/// </summary>
	[JsonIgnore, Hide]
	public readonly RectInt WithoutPosition => new( 0, 0, Width, Height );

	public static RectInt operator +( RectInt r, in Vector2Int p )
	{
		r.Position += p;
		return r;
	}

	/// <summary>
	/// Return true if the passed rect is partially or fully inside this rect.
	/// </summary>
	/// <param name="rect">The passed rect to test.</param>
	/// <param name="fullyInside"><see langword="true"/> to test if the given rect is completely inside this rect. <see langword="false"/> to test for an intersection.</param>
	public readonly bool IsInside( in RectInt rect, bool fullyInside = false )
	{
		if ( fullyInside )
		{
			return left < rect.left && right > rect.right && top < rect.top && bottom > rect.bottom;
		}

		if ( rect.right < left ) return false;
		if ( rect.left > right ) return false;
		if ( rect.top > bottom ) return false;
		if ( rect.bottom < top ) return false;

		return true;
	}

	/// <summary>
	/// Return true if the passed point is inside this rect.
	/// </summary>
	public readonly bool IsInside( in Vector2Int pos )
	{
		if ( pos.x < left ) return false;
		if ( pos.x > right ) return false;
		if ( pos.y > bottom ) return false;
		if ( pos.y < top ) return false;

		return true;
	}

	/// <summary>
	/// Returns a Rect shrunk in every direction by given values.
	/// </summary>
	public readonly RectInt Shrink( in int left, in int top, in int right, in int bottom )
	{
		var r = this;
		r.left += left;
		r.top += top;
		r.right -= right;
		r.bottom -= bottom;

		return r;
	}

	/// <summary>
	/// Returns a Rect shrunk in every direction by given values on each axis.
	/// </summary>
	public readonly RectInt Shrink( in int x, in int y ) => Shrink( x, y, x, y );

	/// <summary>
	/// Returns a Rect shrunk in every direction by given amount.
	/// </summary>
	public readonly RectInt Shrink( in int amt ) => Shrink( amt, amt, amt, amt );

	/// <summary>
	/// Returns a Rect grown in every direction by given amounts.
	/// </summary>
	public readonly RectInt Grow( in int left, in int top, in int right, in int bottom )
	{
		var r = this;
		r.left -= left;
		r.top -= top;
		r.right += right;
		r.bottom += bottom;

		return r;
	}

	/// <summary>
	/// Returns a Rect grown in every direction by given values on each axis.
	/// </summary>
	public readonly RectInt Grow( in int x, in int y ) => Grow( x, y, x, y );

	/// <summary>
	/// Returns a Rect grown in every direction by given amount.
	/// </summary>
	public readonly RectInt Grow( in int amt ) => Grow( amt, amt, amt, amt );

	public static RectInt operator +( in RectInt a, in RectInt b )
	{
		return new RectInt( a.left + b.left, a.top + b.top, a.Width + b.Width, a.Height + b.Height );
	}

	public static RectInt operator *( in RectInt a, in int b )
	{
		return new RectInt( a.left * b, a.top * b, a.Width * b, a.Height * b );
	}

	public static RectInt operator *( in RectInt a, in Vector2Int b )
	{
		return new RectInt( a.left * b.x, a.top * b.y, a.Width * b.x, a.Height * b.y );
	}

	/// <summary>
	/// Create a rect between two points. The order of the points doesn't matter.
	/// </summary>
	public static RectInt FromPoints( in Vector2Int a, in Vector2Int b )
	{
		var topLeft = Vector2Int.Min( a, b );

		return new RectInt( topLeft.x, topLeft.y, Math.Abs( a.x - b.x ), Math.Abs( a.y - b.y ) );
	}

	public override readonly string ToString() => $"{left},{top},{Width},{Height}";

	/// <summary>
	/// Expand this Rect to contain the other rect
	/// </summary>
	public void Add( RectInt r )
	{
		left = Math.Min( left, r.left );
		right = Math.Max( right, r.right );
		top = Math.Min( top, r.top );
		bottom = Math.Max( bottom, r.bottom );
	}

	/// <summary>
	/// Expand this Rect to contain the point
	/// </summary>
	public void Add( Vector2Int point )
	{
		left = Math.Min( left, point.x );
		right = Math.Max( right, point.x );
		top = Math.Min( top, point.y );
		bottom = Math.Max( bottom, point.y );
	}

	/// <summary>
	/// Returns this rect expanded to include this point
	/// </summary>
	public readonly RectInt AddPoint( Vector2Int pos )
	{
		var r = this;
		r.Add( pos );

		return r;
	}

	/// <summary>
	/// Position of the bottom left edge of this rect.
	/// </summary>
	[JsonIgnore, Hide]
	public readonly Vector2Int BottomLeft => new( left, bottom );

	/// <summary>
	/// Position of the bottom right edge of this rect.
	/// </summary>
	[JsonIgnore, Hide]
	public readonly Vector2Int BottomRight => new( right, bottom );

	/// <summary>
	/// Position of the top right edge of this rect.
	/// </summary>
	[JsonIgnore, Hide]
	public readonly Vector2Int TopRight => new( right, top );

	/// <summary>
	/// Position of the top left edge of this rect.
	/// </summary>
	[JsonIgnore, Hide]
	public readonly Vector2Int TopLeft => new( left, top );

	#region equality

	public static bool operator ==( RectInt left, RectInt right ) => left.Equals( right );
	public static bool operator !=( RectInt left, RectInt right ) => !(left == right);
	public override bool Equals( object obj ) => obj is RectInt o && Equals( o );
	public readonly bool Equals( RectInt o ) => (left, right, top, bottom) == (o.left, o.right, o.top, o.bottom);
	public readonly override int GetHashCode() => HashCode.Combine( left, right, top, bottom );

	#endregion
}
