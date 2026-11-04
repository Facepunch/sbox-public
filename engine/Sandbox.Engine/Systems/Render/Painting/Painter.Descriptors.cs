using Sandbox.UI;
using Sandbox.Rendering;

namespace Sandbox;

public readonly ref partial struct Painter
{
	internal record struct BoxDescriptor( Rect Rect, Color Color )
	{
		/// <summary>
		/// Corner radii, resolved. What the renderer draws with.
		/// </summary>
		internal BorderRadii Radii;

		/// <summary>
		/// Circular corner radii as (bottom-right, top-right, bottom-left, top-left).
		/// </summary>
		public Vector4 BorderRadius
		{
			readonly get => Radii.ToPublic();
			set => Radii = BorderRadii.FromPublic( value );
		}

		/// <summary>
		/// Resolved rectangle stroke and optional nine-slice image.
		/// </summary>
		public BoxStroke Stroke;
		public NineSliceImage BorderImage;

		/// <summary>
		/// Which box the background paints into.
		/// </summary>
		public BackgroundClip BackgroundClip;

		/// <summary>
		/// Inset of the clip box from the border box, as left, top, right, bottom.
		/// </summary>
		internal Vector4 BackgroundClipInset;

		/// <summary>
		/// Text the background is clipped to, and where it sits as x, y, w, h relative to Rect.
		/// </summary>
		internal Texture TextMask;
		internal Vector4 TextMaskRect;

		public Texture BackgroundImage;
		public Vector4 BackgroundRect;
		public Color BackgroundTint;
		public float BackgroundAngle;
		public BackgroundRepeat BackgroundRepeat;
		public FilterMode FilterMode;

		internal BlendMode BackgroundBlendMode;
		internal BlendMode OverrideBlendMode;

		/// <summary>
		/// A shader-evaluated background gradient. Mutually exclusive with BackgroundImage.
		/// </summary>
		internal GradientInfo BackgroundGradient;

		internal UICssBoxBatched.BorderShape BorderShapeData;
		internal Painter.Path.Data PathData;
		// A shape the batcher already holds. Only valid in the batcher that received this immediately submitted descriptor.
		internal int? ShapeIndex;

		internal readonly bool HasImage => BackgroundImage != null && BackgroundImage != Sandbox.Texture.Invalid;
		internal readonly bool HasGradient => !BackgroundGradient.ColorOffsets.IsDefaultOrEmpty;
		internal readonly bool HasBorderImage => BorderImage.Texture != null;
		internal readonly bool HasTextMask => TextMask != null && TextMask != Sandbox.Texture.Invalid;
		internal readonly bool HasBorderShape => BorderShapeData.Kind != UICssBoxBatched.ShapeKind.None;

		/// <summary>
		/// Packs a polygon with coordinates relative to the box's top-left.
		/// </summary>
		internal void SetPolygon( ReadOnlySpan<Vector2> points )
			=> CreatePolygonShape( points, out BorderShapeData );

		internal static void CreatePolygonShape( ReadOnlySpan<Vector2> points, out UICssBoxBatched.BorderShape shape )
		{
			ArgumentOutOfRangeException.ThrowIfLessThan( points.Length, 3 );
			ArgumentOutOfRangeException.ThrowIfGreaterThan( points.Length, BorderShape.MaxPoints );

			// Unused slots participate in shape hashing.
			Span<Vector2> padded = stackalloc Vector2[BorderShape.MaxPoints];
			padded.Clear();
			points.CopyTo( padded );

			shape = new UICssBoxBatched.BorderShape
			{
				Kind = UICssBoxBatched.ShapeKind.Polygon,
				Polygon01 = Pack( padded, 0 ),
				Polygon23 = Pack( padded, 2 ),
				Polygon45 = Pack( padded, 4 ),
				Polygon67 = Pack( padded, 6 ),
				PolygonCount = points.Length,
			};
		}

		static Vector4 Pack( ReadOnlySpan<Vector2> points, int i ) => new( points[i].x, points[i].y, points[i + 1].x, points[i + 1].y );

	}

	/// <summary>
	/// Resolved edge colors, widths and style for the combined box shader.
	/// </summary>
	internal record struct BoxStroke
	{
		/// <summary>
		/// Edge widths as left, top, right, bottom.
		/// </summary>
		public Vector4 Size;
		internal BorderStyle Style;
		public Color ColorL;
		public Color ColorT;
		public Color ColorR;
		public Color ColorB;

		internal readonly bool HasInk => Style is not (BorderStyle.None or BorderStyle.Hidden)
			&& (Size.x > 0 && ColorL.a != 0 || Size.y > 0 && ColorT.a != 0
				|| Size.z > 0 && ColorR.a != 0 || Size.w > 0 && ColorB.a != 0);

		internal readonly BoxStroke WithAlphaMultiplied( float opacity ) => this with
		{
			ColorL = ColorL.WithAlphaMultiplied( opacity ),
			ColorT = ColorT.WithAlphaMultiplied( opacity ),
			ColorR = ColorR.WithAlphaMultiplied( opacity ),
			ColorB = ColorB.WithAlphaMultiplied( opacity ),
		};
	}

	/// <summary>
	/// Resolve the panel's border shape against its rect, ready for the batched box shader.
	/// Coordinates come out relative to the box's top-left, not in layout space, so the same
	/// shape on two panels resolves identically wherever they sit and shares one table entry.
	/// It's also what <see cref="UI.Panel.IsInside(Vector2)"/> hit tests against.
	/// </summary>
	internal static void SetBorderShape( ref Painter.BoxDescriptor desc, BorderShape shape )
	{
		desc.BorderShapeData = default;
		if ( shape?.IsNone != false ) return;

		desc.BorderShapeData.Kind = (int)shape.Kind;

		if ( shape.Kind == BorderShapeKind.Circle )
		{
			var circle = shape.ResolveCircle( new Rect( Vector2.Zero, desc.Rect.Size ) );
			desc.BorderShapeData.Circle = new Vector4( circle.Center.x, circle.Center.y, circle.Radius, 0 );
			return;
		}

		Span<Vector2> points = stackalloc Vector2[BorderShape.MaxPoints];

		for ( int i = 0; i < shape.Points.Count; i++ )
		{
			points[i] = new Vector2(
				shape.Points[i].X.GetPixels( desc.Rect.Width ),
				shape.Points[i].Y.GetPixels( desc.Rect.Height ) );
		}

		desc.SetPolygon( points[..shape.Points.Count] );
	}

	/// <summary>
	/// A box-shadow. For an outset shadow Rect is the border box, for an inset one it's the padding box the
	/// shadow is drawn inside; Radii are that box's corners.
	/// </summary>
	internal record struct ShadowDescriptor( Rect Rect, Color Color )
	{
		/// <summary>
		/// Corner radii of Rect, resolved. What the renderer draws with.
		/// </summary>
		internal BorderRadii Radii;

		/// <summary>
		/// Circular corner radii as (top-left, top-right, bottom-left, bottom-right).
		/// </summary>
		public Vector4 BorderRadius
		{
			readonly get => Radii.ToVector4();
			set => Radii = BorderRadii.FromCorners( value );
		}

		public Vector2 Offset;
		public float Blur;
		public float Spread;
		public bool Inset;

		internal BlendMode OverrideBlendMode;

	}

	internal record struct OutlineDescriptor( Rect Rect, Color Color, float Width )
	{
		/// <summary>
		/// Corner radii of Rect, resolved. What the renderer draws with.
		/// </summary>
		internal BorderRadii Radii;

		/// <summary>
		/// Circular corner radii as (top-left, top-right, bottom-left, bottom-right).
		/// </summary>
		public Vector4 BorderRadius
		{
			readonly get => Radii.ToVector4();
			set => Radii = BorderRadii.FromCorners( value );
		}

		public float Offset;

		internal BlendMode OverrideBlendMode;

	}
}
