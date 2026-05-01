using NativeEngine;
using Sandbox.Rendering;
using System.Runtime.InteropServices;

namespace Sandbox;

public static partial class Graphics
{
	const int maxDynamicVertexBuffer = 512 * 1024;

	/// <summary>
	/// This is our entry point into the engine for all draws
	/// </summary>
	internal static unsafe void DrawInternal<T>( T* vertices, int vertCount, ushort* indices, int indexCount, Material material, RenderAttributes attributes = null, PrimitiveType primitiveType = PrimitiveType.Triangles ) where T : unmanaged
	{
		ArgumentNullException.ThrowIfNull( material, nameof( material ) );

		attributes ??= Attributes;

		AssertRenderBlock();

		if ( !SceneLayer.IsValid ) return;

		// Get the layout for this vertex. This will create if not found.
		var vertexType = VertexLayout.Get<T>();
		if ( !vertexType.IsValid ) return;

		// Set the material etc
		if ( !RenderTools.SetRenderState( Context, attributes.Get(), material.native.GetMode( SceneLayer ), vertexType, Graphics.Stats ) )
			return;

		var totalSize = sizeof( T ) * vertCount;
		if ( indices is not null || totalSize < maxDynamicVertexBuffer )
		{
			RenderTools.Draw( Context, (NativeEngine.RenderPrimitiveType)primitiveType, vertexType, (IntPtr)vertices, vertCount, (IntPtr)indices, indexCount, Graphics.Stats );
			return;
		}

		int verticesPerChunk = maxDynamicVertexBuffer / sizeof( T );

		// if we're drawing triangles, and have no indices, round to chunks of 3 so we don't
		// get geometry split between chunks.. because that would be a disaster
		if ( primitiveType == PrimitiveType.Triangles && indices is null )
		{
			verticesPerChunk = verticesPerChunk.SnapToGrid( 3 );
		}

		//
		// todo - for lines chunk size needs to be multiple of 2, for triangles multiple of 3
		//

		for ( int i = 0; i < vertCount; i += verticesPerChunk )
		{
			var chunkCount = Math.Min( verticesPerChunk, vertCount - i );
			RenderTools.Draw( Context, (NativeEngine.RenderPrimitiveType)primitiveType, vertexType, (IntPtr)(vertices + i), chunkCount, default, default, Graphics.Stats );
		}
	}

	/// <summary>
	/// Draw a bunch of vertices
	/// </summary>
	public static unsafe void Draw( Span<Vertex> vertices, int vertCount, Material material, RenderAttributes attributes = null, PrimitiveType primitiveType = PrimitiveType.Triangles )
	{
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
		if ( !SceneLayer.IsValid ) return;
		if ( !obj.IsValid() ) return;

		var tx = transform ?? obj.Transform;
		var cl = color ?? Color.White;
		var mat = material?.native ?? default;

		var attributes = Attributes;

		RenderTools.DrawSceneObject( Context, SceneLayer, obj, tx, cl, mat, attributes.Get() );
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
		var texture = TextRendering.GetOrCreateTexture( scope, flag: flags );

		Attributes.Set( "Texture", texture );
		Attributes.Set( "SamplerIndex", SamplerState.GetBindlessIndex( new SamplerState() { Filter = scope.FilterMode } ) );

		var rect = position.Align( texture.Size, flags );
		DrawQuad( rect.Floor(), Material.UI.Text, Color.White );

		return rect;
	}

	/// <summary>
	/// Draws a rotated text quad in screenspace using the Material.UI.Text material.
	/// </summary>
	internal static void DrawText( in Rect position, float angle, in TextRendering.Scope scope, TextFlag flags = TextFlag.Center )
	{
		var texture = TextRendering.GetOrCreateTexture( scope, flag: flags );

		Attributes.Set( "Texture", texture );
		Attributes.Set( "SamplerIndex", SamplerState.GetBindlessIndex( new SamplerState() { Filter = scope.FilterMode } ) );

		var rect = position.Align( texture.Size, flags );
		DrawQuad( rect, angle, Material.UI.Text, Color.White );
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
		var block = TextRendering.GetOrCreateTexture( scope, position.Size, flags );
		var rect = new Rect( position.Position, block.Size );
		return rect;
	}

	/// <summary>
	/// Calls DrawText with "Material Icons" font. You can get a list of icons here https://fonts.google.com/icons?selected=Material+Icons
	/// </summary>
	public static Rect DrawIcon( Rect rect, string iconName, Color color, float fontSize = 20.0f, TextFlag alignment = TextFlag.Center )
	{
		if ( string.IsNullOrEmpty( iconName ) )
			iconName = "people";

		return DrawText( rect, iconName?.ToLower(), color, "Material Icons", fontSize, flags: alignment );
	}

	/// <summary>
	/// Draw a rounded rectangle, with optional border, in Material.UI.Box
	/// </summary>
	public static unsafe void DrawRoundedRectangle( in Rect rect, in Color color, in Vector4 cornerRadius = default, in Vector4 borderWidth = default, in Color borderColor = default )
	{
		Attributes.Set( "BoxPosition", new Vector2( rect.Left, rect.Top ) );
		Attributes.Set( "BoxSize", new Vector2( rect.Width, rect.Height ) );
		Attributes.Set( "BorderRadius", cornerRadius );
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

		DrawQuad( rect, Material.UI.Box, color );
	}

}
