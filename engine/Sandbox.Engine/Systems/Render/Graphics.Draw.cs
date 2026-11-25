using NativeEngine;
using Sandbox.Rendering;
using System.Runtime.InteropServices;

namespace Sandbox;

public static partial class Graphics
{
	/// <summary>
	/// This is our entry point into the engine for all draws
	/// </summary>
	internal static unsafe void DrawInternal<T>( T* vertices, int vertCount, ushort* indices, int indexCount, Material material, RenderAttributes attributes = null, PrimitiveType primitiveType = PrimitiveType.Triangles ) where T : unmanaged
	{
		ArgumentNullException.ThrowIfNull( material, nameof( material ) );

		attributes ??= Attributes;

		AssertRenderBlock();

		Backend.DrawVertices( typeof( T ), sizeof( T ), (IntPtr)vertices, vertCount, (IntPtr)indices, indexCount, material, attributes, primitiveType );
	}

	internal static void ValidateDrawCounts( int vertCount, int vertexLength, int indexCount = 0, int indexLength = 0 )
	{
		if ( vertCount < 0 || vertCount > vertexLength )
			throw new ArgumentOutOfRangeException( nameof( vertCount ), $"{nameof( vertCount )} ({vertCount}) must be between 0 and the number of vertices ({vertexLength})" );

		if ( indexCount < 0 || indexCount > indexLength )
			throw new ArgumentOutOfRangeException( nameof( indexCount ), $"{nameof( indexCount )} ({indexCount}) must be between 0 and the number of indices ({indexLength})" );
	}

	/// <summary>
	/// Draw a bunch of vertices
	/// </summary>
	public static unsafe void Draw( Span<Vertex> vertices, int vertCount, Material material, RenderAttributes attributes = null, PrimitiveType primitiveType = PrimitiveType.Triangles )
	{
		ValidateDrawCounts( vertCount, vertices.Length );

		fixed ( Vertex* ptr = vertices )
		{
			DrawInternal( ptr, vertCount, default, default, material, attributes, primitiveType );
		}
	}

	/// <summary>
	/// Draw a bunch of vertices
	/// TODO: make this public
	/// TODO: Is this safe to be public
	/// TODO: Is VertexLayoutManager.Get Safe 
	/// </summary>
	internal static unsafe void Draw<T>( Span<T> vertices, int vertCount, Material material, RenderAttributes attributes = null, PrimitiveType primitiveType = PrimitiveType.Triangles ) where T : unmanaged
	{
		ValidateDrawCounts( vertCount, vertices.Length );

		fixed ( T* ptr = vertices )
		{
			DrawInternal( ptr, vertCount, default, default, material, attributes, primitiveType );
		}
	}

	/// <summary>
	/// Draw a bunch of vertices
	/// </summary>
	public static unsafe void Draw( List<Vertex> vertices, int vertCount, Material material, RenderAttributes attributes = null, PrimitiveType primitiveType = PrimitiveType.Triangles )
	{
		Draw( CollectionsMarshal.AsSpan( vertices ), vertCount, material, attributes, primitiveType );
	}

	/// <summary>
	/// Draw a bunch of vertices
	/// TODO: make this public
	/// TODO: Is this safe to be public
	/// TODO: Is VertexLayoutManager.Get Safe 
	/// </summary>
	internal static unsafe void Draw<T>( List<T> vertices, int vertCount, Material material, RenderAttributes attributes = null, PrimitiveType primitiveType = PrimitiveType.Triangles ) where T : unmanaged
	{
		Draw<T>( CollectionsMarshal.AsSpan( vertices ), vertCount, material, attributes, primitiveType );
	}

	/// <summary>
	/// Draw a bunch of vertices
	/// </summary>
	public static unsafe void Draw( Span<Vertex> vertices, int vertCount, Span<ushort> indices, int indexCount, Material material, RenderAttributes attributes = null, PrimitiveType primitiveType = PrimitiveType.Triangles )
	{
		ValidateDrawCounts( vertCount, vertices.Length, indexCount, indices.Length );

		fixed ( Vertex* vptr = vertices )
		fixed ( ushort* iptr = indices )
		{
			DrawInternal( vptr, vertCount, iptr, indexCount, material, attributes, primitiveType );
		}
	}

	static Vertex[] screenQuad;

	/// <summary>
	/// Draw a screen space quad using the passed material. Your material should be using a
	/// screenspace shader so it will actually render to the screen and not in worldspace at 0,0,0
	/// </summary>
	public static void Blit( Material material, RenderAttributes attributes = null )
	{
		if ( screenQuad == null )
		{
			screenQuad = new Vertex[4];
			screenQuad[0].Position = new Vector3( -1, -1, 0.5f );
			screenQuad[0].Normal = new Vector3( 0, 0, -1 );
			screenQuad[0].TexCoord0 = new Vector4( 0, 1, 0, 0 );

			screenQuad[1].Position = new Vector3( 1, -1, 0.5f );
			screenQuad[1].Normal = new Vector3( 0, 0, -1 );
			screenQuad[1].TexCoord0 = new Vector4( 1, 1, 1, 0 );

			screenQuad[2].Position = new Vector3( -1, 1, 0.5f );
			screenQuad[2].Normal = new Vector3( 0, 1, -1 );
			screenQuad[2].TexCoord0 = new Vector4( 0, 0, 0, 1 );

			screenQuad[3].Position = new Vector3( 1, 1, 0.5f );
			screenQuad[3].Normal = new Vector3( 0, 0, -1 );
			screenQuad[3].TexCoord0 = new Vector4( 1, 0, 1, 1 );
		}

		Draw( screenQuad.AsSpan(), 4, material, attributes, PrimitiveType.TriangleStrip );
	}

	/// <summary>
	/// Render a SceneObject
	/// </summary>
	public static void Render( SceneObject obj, Transform? transform = null, Color? color = null, Material material = null )
	{
		AssertRenderBlock();

		if ( !obj.IsValid() ) return;

		// Native draws it through its layer's view; a Borealis frame draws it from its own copy of the object. On the render
		// thread the object's transform is its render object's, as the sync point wrote it: the game may be moving it meanwhile
		var at = transform ?? (Rendering.RenderSync.OnRenderThread && obj.RenderObject is { } drawn ? drawn.Transform : obj.Transform);
		_state.view?.RenderSceneObject( obj, at, color ?? Color.White, material, Attributes );
	}

	/// <summary>
	/// Draw a quad in screenspace
	/// </summary>
	public static unsafe void DrawQuad( in Rect rect, in Material material, in Color color, RenderAttributes attributes = null )
	{
		attributes ??= Attributes;

		var color32 = color.ToColor32();

		var vertices = stackalloc Vertex[4]
		{
			new Vertex( new Vector2( rect.Left, rect.Top ), new Vector2( 0, 0 ), color32 ),
			new Vertex( new Vector2( rect.Right, rect.Top ), new Vector2( 1, 0 ), color32 ),
			new Vertex( new Vector2( rect.Left, rect.Bottom ), new Vector2( 0, 1 ), color32 ),
			new Vertex( new Vector2( rect.Right, rect.Bottom ), new Vector2( 1, 1 ), color32 )
		};

		DrawInternal( vertices, 4, default, default, material, attributes, PrimitiveType.TriangleStrip );
	}

	/// <summary>
	/// Draw a rotated quad in screenspace
	/// </summary>
	internal static unsafe void DrawQuad( in Rect rect, in float angle, in Material material, in Color color, RenderAttributes attributes = null )
	{
		attributes ??= Attributes;

		var color32 = color.ToColor32();

		var rads = angle.DegreeToRadian();
		var cos = MathF.Cos( rads );
		var sin = MathF.Sin( rads );

		var origin = rect.Center;
		var addX = new Vector2( cos, sin ) * rect.Width * 0.5f;
		var addY = new Vector2( -sin, cos ) * rect.Height * 0.5f;

		var vertices = stackalloc Vertex[4]
		{
			new Vertex( origin - addX - addY, new Vector2( 0, 0 ), color32 ),
			new Vertex( origin + addX - addY, new Vector2( 1, 0 ), color32 ),
			new Vertex( origin - addX + addY, new Vector2( 0, 1 ), color32 ),
			new Vertex( origin + addX + addY, new Vector2( 1, 1 ), color32 )
		};

		DrawInternal( vertices, 4, default, default, material, attributes, PrimitiveType.TriangleStrip );
	}

	/// <summary>
	/// Draws a text quad in screenspace using the Material.UI.Text material.
	/// </summary>
	public static Rect DrawText( in Rect position, string text, Color color, string fontFamily = "Roboto", float fontSize = 20.0f, float fontWeight = 450, TextFlag flags = TextFlag.Center )
	{
		return DrawText( position, new TextRendering.Scope( text, color, fontSize, fontFamily, (int)fontWeight ), flags );
	}

	/// <summary>
	/// Draws a text quad in screenspace using the Material.UI.Text material.
	/// </summary>
	public static Rect DrawText( in Rect position, in TextRendering.Scope scope, TextFlag flags = TextFlag.Center )
	{
		var block = TextRendering.GetOrCreateTextBlock( scope, flags );
		return block is null ? position : DrawText( block, position, flags, 0 );
	}

	/// <summary>
	/// Draws a rotated text quad in screenspace using the Material.UI.Text material.
	/// </summary>
	internal static void DrawText( in Rect position, float angle, in TextRendering.Scope scope, TextFlag flags = TextFlag.Center )
	{
		var block = TextRendering.GetOrCreateTextBlock( scope, flags );
		if ( block is not null ) DrawText( block, position, flags, angle );
	}

	/// <summary>
	/// Draws a text block aligned in position as one quad with Material.UI.Text, composited per pixel from its glyph outlines.
	/// </summary>
	internal static Rect DrawText( TextRendering.TextBlock block, in Rect position, TextFlag flags, float angle )
	{
		var rect = position.Align( block.Size, flags );
		if ( block.IsEmpty ) return rect;

		GpuFontText.Bind( Attributes, block.Upload() );

		if ( angle == 0f )
			DrawQuad( rect.Floor(), Material.UI.Text, Color.White );
		else
			DrawQuad( rect.Floor(), angle, Material.UI.Text, Color.White );

		return rect;
	}

	/// <summary>
	/// Draws a text quad in screenspace using the Material.UI.Text material.
	/// </summary>
	public static Rect DrawText( in Vector2 position, string text, Color color, string fontFamily = "Roboto", float fontSize = 20.0f, float fontWeight = 450 )
	{
		if ( string.IsNullOrEmpty( text ) ) text = null;

		return DrawText( new Rect( position, 4096 ), text, color, fontFamily, fontSize, fontWeight, TextFlag.LeftTop );
	}

	/// <summary>
	/// Measure how big some text will be, without having to render it
	/// </summary>
	public static Rect MeasureText( in Rect position, string text, string fontFamily = "Roboto", float fontSize = 20.0f, float fontWeight = 450, TextFlag flags = TextFlag.Center )
	{
		return MeasureText( position, new TextRendering.Scope( text, Color.White, fontSize, fontFamily, (int)fontWeight ), flags );
	}

	/// <summary>
	/// Measure how big some text will be, without having to render it
	/// </summary>
	public static Rect MeasureText( in Rect position, in TextRendering.Scope scope, TextFlag flags = TextFlag.Center )
	{
		var block = TextRendering.GetOrCreateTextBlock( scope, flags, position.Size );
		return block is null ? position : new Rect( position.Position, block.Size );
	}

	/// <summary>
	/// Calls DrawText with "Material Icons" font. You can get a list of icons here https://fonts.google.com/icons?selected=Material+Icons
	/// </summary>
	public static Rect DrawIcon( Rect rect, string iconName, Color color, float fontSize = 20.0f, TextFlag alignment = TextFlag.Center )
	{
		if ( string.IsNullOrEmpty( iconName ) )
			iconName = "people";

		return DrawText( rect, iconName?.ToLower(), color, "Material Symbols Outlined", fontSize, flags: alignment );
	}

	/// <summary>
	/// Draw a rounded rectangle, with optional border, in Material.UI.Box. Corner radii are
	/// (bottom-right, top-right, bottom-left, top-left); radii too big for the rect are scaled to fit, like CSS.
	/// Border widths are left, top, right, bottom.
	/// </summary>
	public static unsafe void DrawRoundedRectangle( in Rect rect, in Color color, in Vector4 cornerRadius = default, in Vector4 borderWidth = default, in Color borderColor = default )
	{
		var radii = BorderRadii.FromPublic( cornerRadius ).Clamped( rect.Width, rect.Height );

		Attributes.Set( "BoxPosition", new Vector2( rect.Left, rect.Top ) );
		Attributes.Set( "BoxSize", new Vector2( rect.Width, rect.Height ) );
		Attributes.Set( "BoxBloat", 1.0f );
		Attributes.Set( "BorderRadius", radii.Horizontal );
		Attributes.Set( "BorderRadiusV", radii.Vertical );
		Attributes.SetCombo( "D_BACKGROUND_IMAGE", 0 );

		if ( !borderWidth.IsNearZeroLength )
		{
			Attributes.Set( "HasBorder", 1 );
			Attributes.SetCombo( "D_BORDER_IMAGE", 0 );

			Attributes.Set( "BorderSize", borderWidth );
			Attributes.Set( "BorderColorL", borderColor );
			Attributes.Set( "BorderColorT", borderColor );
			Attributes.Set( "BorderColorR", borderColor );
			Attributes.Set( "BorderColorB", borderColor );
		}
		else
		{
			Attributes.Set( "HasBorder", 0 );
			Attributes.SetCombo( "D_BORDER_IMAGE", 0 );
		}

		DrawQuad( rect.Grow( 1 ), Material.UI.Box, color );

		// Attributes are global, don't leave the bloat on for anyone drawing with Material.UI.Box themselves
		Attributes.Set( "BoxBloat", 0.0f );
	}

}
