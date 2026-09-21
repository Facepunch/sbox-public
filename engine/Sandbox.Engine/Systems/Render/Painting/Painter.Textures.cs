using Sandbox.Rendering;
using Sandbox.UI;

namespace Sandbox;

public readonly ref partial struct Painter
{
	/// <summary>
	/// Draws text with the current TextStyle, opacity, transform and clip.
	/// </summary>
	public void Text( string text, Rect rect ) => DrawText( text, rect, TextStyle );

	/// <summary>
	/// Draws a texture within the given rectangle using bilinear sampling, matching Fill.Image's default.
	/// </summary>
	/// <param name="texture">The texture to draw.</param>
	/// <param name="rect">Destination rectangle in drawing coordinates.</param>
	/// <param name="tint">Optional color tint applied to the texture. Defaults to <see cref="Color.White"/> (no tint).</param>
	public void Texture( Texture texture, Rect rect, Color? tint = null )
		=> Texture( texture, rect, tint ?? Color.White, FilterMode.Bilinear );

	/// <summary>
	/// Draw an image with an explicit sampler using the current drawing state.
	/// </summary>
	internal void Texture( Texture texture, Rect rect, Color tint, FilterMode filterMode )
	{
		Add( ActiveContext, new BoxDescriptor( rect, Color.Transparent )
		{
			BackgroundImage = texture,
			BackgroundTint = tint.WithAlphaMultiplied( ActiveContext.InheritedOpacity ),
			BackgroundRepeat = BackgroundRepeat.Clamp,
			FilterMode = filterMode,
		} );
	}

	internal void DrawText( string text, Rect rect, TextStyle style, bool legacy = false )
	{
		var context = ActiveContext;
		var opacity = style.Color.a * context.InheritedOpacity * (legacy ? 1 : context.State.Opacity);
		if ( opacity == 0 || (!legacy && !context.State.HasArea) ) return;
		var block = GetTextBlock( text, rect.Size, style );
		if ( block is null || block.IsEmpty ) return;
		block.EnsureLayout();
		var textRect = rect.Align( block.Size, style.Alignment ).Floor();
		var glyphs = context.TextInstances;
		glyphs.Clear();
		var options = GpuFontText.Options.For( style.CreateScope( text, context.ScaleToScreen ) );
		GpuFontText.Build( block.Layout, textRect.Position + block.BlockOrigin, options, glyphs );
		context.Batcher.AddText( glyphs, legacy ? context.InitialBlendMode : context.State.OverrideBlendMode, default,
			legacy ? Matrix.Identity : DrawingTransform( context ), legacy ? -1 : context.State.ClipIndex,
			opacity );
	}

	/// <summary>
	/// Draws a laid-out label using GPU outlines in panel layout coordinates.
	/// </summary>
	internal void Glyphs( List<GPUBoxInstance> glyphs, BlendMode blendMode, in TextGradientInfo gradient )
		=> GetActiveContext().Batcher.AddText( glyphs, blendMode, gradient, Matrix.Identity, -1 );

	/// <summary>
	/// Draws an inset or outset rectangle shadow. Corners accept a single radius or elliptical
	/// CornerRadii, with the same ordering and radius clamping as Rect.
	/// Drawing opacity, transform and clipping apply.
	/// </summary>
	/// <param name="rect">The rectangle to cast the shadow from, in drawing coordinates.</param>
	/// <param name="corners">Corner radii matching Rect. Defaults to square corners.</param>
	/// <param name="color">Shadow color. Defaults to black.</param>
	/// <param name="blur">Blur radius in drawing pixels. Higher values produce softer shadows.</param>
	/// <param name="spread">Spread in drawing pixels. Positive values expand the shadow; negative values shrink it.</param>
	/// <param name="offset">Shadow offset in drawing pixels.</param>
	/// <param name="inset">Draw an inner shadow instead of a drop shadow.</param>
	public void RectShadow( Rect rect, CornerRadii corners = default, Color? color = null, float blur = 0, float spread = 0, Vector2 offset = default, bool inset = false )
		=> Add( ActiveContext, new ShadowDescriptor( rect, (color ?? Color.Black).WithAlphaMultiplied( ActiveContext.InheritedOpacity ) )
		{
			Radii = corners.Resolve( rect ),
			Blur = blur,
			Spread = spread,
			Offset = offset,
			Inset = inset,
		} );

	internal void RectShadow( Rect rect, in BorderRadii radii, Color color, float blur, float spread, Vector2 offset, bool inset )
	{
		var context = GetActiveContext();
		context.Batcher.Add( new ShadowDescriptor( rect, color.WithAlphaMultiplied( context.InheritedOpacity ) )
		{
			Radii = radii.Clamped( rect.Width, rect.Height ),
			Blur = blur,
			Spread = spread,
			Offset = offset,
			Inset = inset,
			OverrideBlendMode = context.InitialBlendMode
		}, Matrix.Identity, -1 );
	}

	/// <summary>
	/// Measures text with the current TextStyle, including glyph overhang, in the same coordinates as Painter.Text.
	/// maxSize supplies the same layout bounds as a drawing rectangle; default uses Painter.Text's unconstrained bounds.
	/// The result includes ScaleToScreen but excludes Painter.Transform, clipping and opacity. Empty text returns zero.
	/// Prepares cached layout without recording a draw or allocating a bitmap or GPU texture. A later draw reuses the layout.
	/// </summary>
	public Vector2 MeasureText( string text, Vector2 maxSize = default )
	{
		if ( maxSize != default && (!maxSize.IsFinite || maxSize.x <= 0 || maxSize.y <= 0) )
			throw new ArgumentOutOfRangeException( nameof( maxSize ), "Expected positive finite layout bounds, or default for unconstrained text." );
		return GetTextBlock( text, maxSize, TextStyle )?.Measure() ?? Vector2.Zero;
	}

	/// <summary>
	/// Returns the aligned, pixel-rounded rectangle that Painter.Text(text, rect) will use with the current TextStyle.
	/// Measures before Painter.Transform or clipping. Empty text produces an empty rectangle at rect.Position.
	/// </summary>
	public Rect MeasureText( string text, Rect rect )
	{
		var size = MeasureText( text, rect.Size );
		return size == default ? new Rect( rect.Position, Vector2.Zero ) : rect.Align( size, TextStyle.Alignment ).Floor();
	}

	TextRendering.TextBlock GetTextBlock( string text, Vector2 size, TextStyle style )
	{
		if ( string.IsNullOrEmpty( text ) ) return null;
		// Keep RGB in the raster, including color glyphs; opacity belongs in the tint.
		var scope = style.CreateScope( text, ActiveContext.ScaleToScreen );
		return TextRendering.GetOrCreateTextBlock( scope, style.Alignment, size == default ? new Vector2( 8096 ) : size );
	}
}
