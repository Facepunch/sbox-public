using Sandbox.Rendering;

namespace Sandbox.UI;

public record struct BoxDrawDescriptor( Rect PanelRect, Color Color )
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

	public Vector4 BorderSize;

	/// <summary>Which box the background paints into.</summary>
	public BackgroundClip BackgroundClip;

	/// <summary>Inset of the clip box from the border box, as left, top, right, bottom.</summary>
	internal Vector4 BackgroundClipInset;

	/// <summary>Text the background is clipped to, and where it sits as x, y, w, h relative to PanelRect.</summary>
	internal Texture TextMask;
	internal Vector4 TextMaskRect;

	public Color BorderColorL;
	public Color BorderColorT;
	public Color BorderColorR;
	public Color BorderColorB;
	public Texture BackgroundImage;
	public Vector4 BackgroundRect;
	public Color BackgroundTint;
	public float BackgroundAngle;
	public BackgroundRepeat BackgroundRepeat;
	public FilterMode FilterMode;

	internal BlendMode BackgroundBlendMode;
	internal BlendMode OverrideBlendMode;

	/// <summary>A shader-evaluated background gradient. Mutually exclusive with BackgroundImage.</summary>
	internal GradientInfo BackgroundGradient;

	internal Texture BorderImageTexture;
	internal Vector4 BorderImageSlice;
	internal BorderImageRepeat BorderImageRepeat;
	internal BorderImageFill BorderImageFill;
	internal Color BorderImageTint;
	internal GPUBorderShape BorderShapeData;

	internal bool HasImage => BackgroundImage != null && BackgroundImage != Texture.Invalid;
	internal bool HasGradient => !BackgroundGradient.ColorOffsets.IsDefaultOrEmpty;
	internal bool HasBorderImage => BorderImageTexture != null;
	internal bool HasTextMask => TextMask != null && TextMask != Texture.Invalid;
	internal bool IsTwoPass => HasImage && BackgroundBlendMode != BlendMode.Normal;
	internal bool HasBorderShape => BorderShapeData.Kind != 0;

	/// <summary>
	/// Resolve the panel's border shape against its rect, ready for the batched box shader.
	/// Coordinates come out relative to the box's top-left, not in layout space, so the same
	/// shape on two panels resolves identically wherever they sit and shares one table entry.
	/// It's also what <see cref="Panel.IsInside(Vector2)"/> hit tests against.
	/// </summary>
	internal void SetBorderShape( BorderShape shape )
	{
		if ( shape?.IsNone != false ) return;

		BorderShapeData.Kind = (int)shape.Kind;

		if ( shape.Kind == BorderShapeKind.Circle )
		{
			var circle = shape.ResolveCircle( new Rect( Vector2.Zero, PanelRect.Size ) );
			BorderShapeData.Circle = new Vector4( circle.Center.x, circle.Center.y, circle.Radius, 0 );
			return;
		}

		// Unused slots stay zeroed, so the shader only ever reads the first PolygonCount of them
		Span<Vector2> points = stackalloc Vector2[BorderShape.MaxPoints];

		for ( int i = 0; i < shape.Points.Count; i++ )
		{
			points[i] = new Vector2(
				shape.Points[i].X.GetPixels( PanelRect.Width ),
				shape.Points[i].Y.GetPixels( PanelRect.Height ) );
		}

		BorderShapeData.Polygon01 = Pack( points, 0 );
		BorderShapeData.Polygon23 = Pack( points, 2 );
		BorderShapeData.Polygon45 = Pack( points, 4 );
		BorderShapeData.Polygon67 = Pack( points, 6 );
		BorderShapeData.PolygonCount = shape.Points.Count;
	}

	static Vector4 Pack( Span<Vector2> points, int i ) => new( points[i].x, points[i].y, points[i + 1].x, points[i + 1].y );

}
