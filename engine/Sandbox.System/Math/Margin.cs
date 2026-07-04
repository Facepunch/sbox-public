using System.Runtime.InteropServices;
using System.Text.Json.Serialization;


#pragma warning disable CS0618 // Type or member is obsolete
namespace Sandbox.UI
{
	/// <summary>
	/// Represents a <see cref="Rect">Rect</see> where each side is the thickness of an edge/padding/margin/border, rather than positions.
	/// </summary>
	[StructLayout( LayoutKind.Sequential )]
	public struct Margin
	{
		private float left;
		private float top;
		private float right;
		private float bottom;

		static public implicit operator Margin( float value ) => new Margin( value );

		public Margin( Rect r )
		{
			this.left = r.Left;
			this.top = r.Top;
			this.right = r.Right;
			this.bottom = r.Bottom;
		}

		public Margin( float uniform )
		{
			this.left = uniform;
			this.top = uniform;
			this.right = uniform;
			this.bottom = uniform;
		}

		public Margin( float horizontal, float vertical )
		{
			this.left = horizontal;
			this.top = vertical;
			this.right = horizontal;
			this.bottom = vertical;
		}

		public Margin( float left, float top, float right, float bottom )
		{
			this.left = left;
			this.top = top;
			this.right = right;
			this.bottom = bottom;
		}

		public Margin( float? left, float? top, float? right, float? bottom )
		{
			this.left = left ?? 0;
			this.top = top ?? 0;
			this.right = right ?? 0;
			this.bottom = bottom ?? 0;
		}

		/// <summary>
		/// Width of the inner square contained within the margin.
		/// </summary>
		[JsonIgnore, Hide]
		public float Width
		{
			get => right - left;
			set => right = left + value;
		}

		/// <summary>
		/// Height of the inner square contained within the margin.
		/// </summary>
		[JsonIgnore, Hide]
		public float Height
		{
			get => bottom - top;
			set => bottom = top + value;
		}

		/// <summary>
		/// Thickness of the left side margin.
		/// </summary>
		[Hide]
		public float Left
		{
			readonly get => left;
			set => left = value;
		}

		/// <summary>
		/// Thickness of the top margin.
		/// </summary>
		[Hide]
		public float Top
		{
			readonly get => top;
			set => top = value;
		}

		/// <summary>
		/// Thickness of the right side margin.
		/// </summary>
		[Hide]
		public float Right
		{
			readonly get => right;
			set => right = value;
		}

		/// <summary>
		/// Thickness of the bottom margin.
		/// </summary>
		[Hide]
		public float Bottom
		{
			readonly get => bottom;
			set => bottom = value;
		}

		/// <summary>
		/// Position of the inner top left corder of the margin/border.
		/// </summary>
		[JsonIgnore, Hide]
		public Vector2 Position
		{
			get => new( left, top );
			set
			{
				var s = Size;
				left = value.x;
				top = value.y;
				Size = s;
			}
		}

		/// <summary>
		/// Size of the inner square contained within the margin.
		/// </summary>
		[JsonIgnore]
		public Vector2 Size
		{
			get => new( Width, Height );
			set
			{
				right = left + value.x;
				bottom = top + value.y;
			}
		}

		/// <summary>
		/// Returns a Rect where left right top bottom describe the size of an edge.
		/// This is used for things like margin, padding, border size.
		/// </summary>
		internal static Margin GetEdges( in Vector2 size, in Length? l, in Length? t, in Length? r, in Length? b )
		{
			return new( l?.GetPixels( size.x ), t?.GetPixels( size.y ), r?.GetPixels( size.x ), b?.GetPixels( size.y ) );
		}

		/// <summary>
		/// When the Rect describes edges, this returns the total size of the edges in each direction
		/// </summary>
		[JsonIgnore]
		public Vector2 EdgeSize => new( left + right, top + bottom );

		/// <summary>
		/// Where padding is an edge type rect, will return this rect expanded with those edges.
		/// </summary>
		public Margin EdgeAdd( Margin edges )
		{
			var r = this;
			r.left += edges.left;
			r.top += edges.top;
			r.right -= edges.right;
			r.bottom -= edges.bottom;

			return r;
		}

		/// <summary>
		/// Where padding is an edge type rect, will return this rect contracted by those edges.
		/// The inverse of <see cref="EdgeAdd"/>.
		/// </summary>
		public Margin EdgeSubtract( Margin edges )
		{
			var r = this;
			r.left -= edges.left;
			r.top -= edges.top;
			r.right += edges.right;
			r.bottom += edges.bottom;

			return r;
		}

		/// <summary>
		/// Returns true if margin is practically zero
		/// </summary>
		public readonly bool IsNearlyZero( double tolerance = 0.000001 )
		{
			return MathF.Abs( left ) <= tolerance && MathF.Abs( right ) <= tolerance && MathF.Abs( top ) <= tolerance && MathF.Abs( bottom ) <= tolerance;
		}

		public static Margin operator +( Margin a, Margin b )
		{
			return new Margin( a.left + b.left, a.top + b.top, a.right + b.right, a.bottom + b.bottom );
		}

		public static Margin operator *( Margin a, float b )
		{
			return new Margin( a.left * b, a.top * b, a.right * b, a.bottom * b );
		}

		public static Margin operator *( Margin a, Vector2 b )
		{
			return new Margin( a.left * b.x, a.top * b.y, a.right * b.x, a.bottom * b.y );
		}

		public override int GetHashCode()
		{
			return HashCode.Combine( left, top, right, bottom );
		}
	}

}

#pragma warning restore CS0618 // Type or member is obsolete

