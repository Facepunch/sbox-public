namespace Sandbox.Rendering;

/// <summary>
/// 2D Drawing functions for a <see cref="CommandList"/>.
/// <para>
/// <c>HudPainter</c> provides a set of methods for drawing shapes, textures, and text onto a command list, typically for HUD or UI rendering.
/// </para>
/// </summary>
public readonly ref struct HudPainter
{
	/// <summary>
	/// The underlying <see cref="CommandList"/> used for rendering.
	/// </summary>
	public readonly CommandList list;

	/// <summary>
	/// Initializes a new instance of the <see cref="HudPainter"/> struct for the specified <paramref name="commandList"/>.
	/// </summary>
	/// <param name="commandList">The command list to draw to. Must not be null.</param>
	public HudPainter( CommandList commandList )
	{
		Assert.NotNull( commandList );
		list = commandList;
	}

	/// <summary>
	/// Sets the blend mode for subsequent drawing operations.
	/// </summary>
	/// <param name="mode">The blend mode to use.</param>
	public void SetBlendMode( BlendMode mode )
	{
		list.Attributes.SetCombo( "D_BLENDMODE", mode );
	}

	/// <summary>
	/// Sets the transformation matrix for subsequent drawing operations.
	/// </summary>
	/// <param name="matrix">The transformation matrix to apply.</param>
	public void SetMatrix( Matrix matrix )
	{
		list.Attributes.Set( "TransformMat", matrix );
	}

	/// <summary>
	/// Draws a filled circle at the specified position and size.
	/// </summary>
	/// <param name="position">The center position of the circle.</param>
	/// <param name="size">The size (diameter) of the circle.</param>
	/// <param name="color">The color of the circle.</param>
	public void DrawCircle( Vector2 position, Vector2 size, Color color )
	{
		var rect = new Rect( position - size * 0.5f, size );
		DrawRect( rect, color, cornerRadius: new Vector4( size.x + size.y, size.x + size.y, size.x + size.y, size.x + size.y ) );
	}

	/// <summary>
	/// Draws a rectangle with optional corner radius and border.
	/// </summary>
	/// <param name="rect">The rectangle to draw.</param>
	/// <param name="color">The fill color of the rectangle.</param>
	/// <param name="cornerRadius">The radius for each corner (optional).</param>
	/// <param name="borderWidth">The width of the border for each edge (optional).</param>
	/// <param name="borderColor">The color of the border (optional).</param>
	public void DrawRect( in Rect rect, in Color color, in Vector4 cornerRadius = default, in Vector4 borderWidth = default, in Color borderColor = default )
	{
		var r = rect.SnapToGrid();

		list.Attributes.Set( "BoxPosition", new Vector2( r.Left, r.Top ) );
		list.Attributes.Set( "BoxSize", new Vector2( r.Width, r.Height ) );
		list.Attributes.Set( "BorderRadius", cornerRadius );
		list.Attributes.SetCombo( "D_BACKGROUND_IMAGE", 0 );

		if ( !borderWidth.IsNearZeroLength )
		{
			list.Attributes.Set( "HasBorder", 1 );
			list.Attributes.SetCombo( "D_BORDER_IMAGE", 0 );

			list.Attributes.Set( "BorderSize", borderWidth );
			list.Attributes.Set( "BorderColorL", borderColor );
			list.Attributes.Set( "BorderColorT", borderColor );
			list.Attributes.Set( "BorderColorR", borderColor );
			list.Attributes.Set( "BorderColorB", borderColor );
		}
		else
		{
			list.Attributes.Set( "HasBorder", 0 );
			list.Attributes.SetCombo( "D_BORDER_IMAGE", 0 );
		}

		list.DrawQuad( r, Material.UI.Box, color );
	}

	/// <summary>
	/// Draws a texture in the specified rectangle with a white tint.
	/// </summary>
	/// <param name="texture">The texture to draw.</param>
	/// <param name="rect">The rectangle to draw the texture in.</param>
	public void DrawTexture( Texture texture, Rect rect ) => DrawTexture( texture, rect, Color.White );

	/// <summary>
	/// Draws a texture in the specified rectangle with a tint color.
	/// </summary>
	/// <param name="texture">The texture to draw.</param>
	/// <param name="rect">The rectangle to draw the texture in.</param>
	/// <param name="tint">The tint color to apply to the texture.</param>
	public void DrawTexture( Texture texture, Rect rect, Color tint )
	{
		list.Attributes.Set( "Texture", texture );
		list.DrawQuad( rect, Material.UI.Basic, tint );
	}

	/// <summary>
	/// Draws text at a 3D point with the specified size, color, and alignment flags.
	/// </summary>
	/// <param name="text">The text to draw.</param>
	/// <param name="size">The font size.</param>
	/// <param name="color">The color of the text.</param>
	/// <param name="point">The 3D point to draw the text at.</param>
	/// <param name="flags">Text alignment flags (optional).</param>
	public void DrawText( string text, float size, Color color, Vector2 point, TextFlag flags = TextFlag.LeftTop )
	{
		var scope = new TextRendering.Scope( text, color, size );
		DrawText( scope, point, flags );
	}

	/// <summary>
	/// Draws text within a rectangle with the specified size, color, and alignment flags.
	/// </summary>
	/// <param name="text">The text to draw.</param>
	/// <param name="size">The font size.</param>
	/// <param name="color">The color of the text.</param>
	/// <param name="rect">The rectangle to draw the text in.</param>
	/// <param name="flags">Text alignment flags (optional).</param>
	public void DrawText( string text, float size, Color color, Rect rect, TextFlag flags = TextFlag.LeftTop )
	{
		var scope = new TextRendering.Scope( text, color, size );
		DrawText( scope, rect, flags );
	}

	/// <summary>
	/// Draws text at a 3D point using a prepared <see cref="TextRendering.Scope"/>.
	/// </summary>
	/// <param name="scope">The text rendering scope.</param>
	/// <param name="point">The 3D point to draw the text at.</param>
	/// <param name="flags">Text alignment flags (optional).</param>
	public Rect DrawText( in TextRendering.Scope scope, Vector2 point, TextFlag flags = TextFlag.LeftTop )
	{
		Rect rect = new Rect( point, 1 );
		return DrawText( scope, rect, flags );
	}

	private static readonly Material TextShader = Material.FromShader( "shaders/ui_text.shader" );

	/// <summary>
	/// Draws text within a rectangle using a prepared <see cref="TextRendering.Scope"/>.
	/// </summary>
	/// <param name="scope">The text rendering scope.</param>
	/// <param name="rect">The rectangle to draw the text in.</param>
	/// <param name="flags">Text alignment flags (optional).</param>
	public Rect DrawText( in TextRendering.Scope scope, Rect rect, TextFlag flags = TextFlag.LeftTop )
	{
		var texture = TextRendering.GetOrCreateTexture( scope, flag: flags );
		if ( texture is null ) return rect;

		list.Attributes.Set( "Texture", texture );
		rect = rect.Align( texture.Size, flags );
		rect = rect.SnapToGrid();

		list.DrawQuad( rect.SnapToGrid(), TextShader, Color.White );
		return rect;
	}

	private static readonly Material LineShader = Material.FromShader( "shaders/Hud/line.shader" );

	/// <summary>
	/// Draws a line between two points with the specified width and color.
	/// </summary>
	/// <param name="a">The start point of the line.</param>
	/// <param name="b">The end point of the line.</param>
	/// <param name="width">The width of the line.</param>
	/// <param name="color">The color of the line.</param>
	/// <param name="corners">Optional corner flags for line end caps.</param>
	public void DrawLine( Vector2 a, Vector2 b, float width, Color color, Vector4 corners = default )
	{
		list.Attributes.Set( "LineStart", a );
		list.Attributes.Set( "LineEnd", b );
		list.Attributes.Set( "LineThickness", width );
		list.Attributes.Set( "ColorStart", color );
		list.Attributes.Set( "ColorEnd", color );
		list.Attributes.Set( "EndCaps", 0 ); // flags 1 2  - use corners
		list.Attributes.Set( "TransformMat", Matrix.Identity );

		list.DrawQuad( NormalizedRect, LineShader, Color.White );
	}

	private static readonly Rect NormalizedRect = new( Vector2.One * -1, Vector2.One * 2 );
}
