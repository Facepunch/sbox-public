using System;

namespace EngineTests;

/// <summary>
/// Compares analytic simple lines against the general path renderer on the GPU.
/// </summary>
[TestClass]
public class PainterSimpleLineTest
{
	/// <summary>
	/// A repeated endpoint forces the reference through the general path, which removes that duplicate.
	/// </summary>
	[TestMethod]
	[DataRow( 48f, 20f, 6f, false )]
	[DataRow( 20f, 48f, 6f, false )]
	[DataRow( 47f, 43f, 6f, true )]
	[DataRow( 12f, 9f, 3f, true )]
	[DataRow( 20.5f, 20.25f, 10f, true )]
	[DataRow( 48f, 35f, 0.75f, true )]
	public void SimpleLineMatchesGeneralPath( float x, float y, float width, bool styled )
	{
		if ( g_pRenderDevice.GetRenderDeviceAPI() != NativeEngine.RenderDeviceAPI_t.RENDER_DEVICE_API_VULKAN )
			Assert.Inconclusive( "Requires Vulkan rendering." );

		foreach ( var cap in Enum.GetValues<Stroke.LineCap>() )
			foreach ( var style in new[] { BorderStyle.Solid, BorderStyle.Dashed, BorderStyle.Dotted } )
				foreach ( float phase in new[] { -9f, 0, 3, 7 } )
				{
					Compare( new Vector2( x, y ), width, styled, cap, style, phase );
				}
	}

	/// <summary>
	/// Dense patterns and caps wider than a period must still union before blending.
	/// </summary>
	[TestMethod]
	[DataRow( 12f, 2f, 1f, false )]
	[DataRow( 0.25f, 0.25f, 0.1f, true )]
	[DataRow( 3f, 5f, 0f, false )]
	public void DensePatternsMatchGeneralPath( float width, float dash, float gap, bool skew )
	{
		if ( g_pRenderDevice.GetRenderDeviceAPI() != NativeEngine.RenderDeviceAPI_t.RENDER_DEVICE_API_VULKAN )
			Assert.Inconclusive( "Requires Vulkan rendering." );

		foreach ( var cap in Enum.GetValues<Stroke.LineCap>() )
			foreach ( var style in new[] { BorderStyle.Dashed, BorderStyle.Dotted } )
			{
				Compare( new Vector2( 47, 43 ), width, true, cap, style, -0.5f, dash, gap, skew );
			}
	}

	static void Compare( Vector2 to, float width, bool styled, Stroke.LineCap cap, BorderStyle style, float phase, float dash = 5, float gap = 3, bool skew = false )
	{
		using var fast = Texture.CreateRenderTarget().WithSize( 64, 64 ).Create();
		using var reference = Texture.CreateRenderTarget().WithSize( 64, 64 ).Create();
		Draw( fast, false, to, width, styled, cap, style, phase, dash, gap, skew );
		Draw( reference, true, to, width, styled, cap, style, phase, dash, gap, skew );
		using var actual = fast.GetBitmap();
		using var expected = reference.GetBitmap();
		float maxDifference = 0;
		float maxAlpha = 0;
		for ( int row = 0; row < 64; row++ )
		{
			for ( int column = 0; column < 64; column++ )
			{
				var a = actual.GetPixel( column, row );
				var b = expected.GetPixel( column, row );
				maxAlpha = MathF.Max( maxAlpha, b.a );
				// RGB outside the covered shape is undefined; compare the composited contribution.
				maxDifference = MathF.Max( maxDifference, MathF.Max( MathF.Abs( a.a - b.a ),
					MathF.Max( MathF.Abs( a.r * a.a - b.r * b.a ), MathF.Max( MathF.Abs( a.g * a.a - b.g * b.a ), MathF.Abs( a.b * a.a - b.b * b.a ) ) ) ) );
			}
		}
		if ( style == BorderStyle.Solid ) Assert.IsTrue( maxAlpha > 0.1f, "The comparison must contain visible pixels." );
		Assert.IsTrue( maxDifference <= 0.02f, $"{style}, {cap}, phase {phase}: maximum pixel difference {maxDifference}" );
	}

	static void Draw( Texture target, bool reference, Vector2 to, float width, bool styled, Stroke.LineCap cap, BorderStyle style, float phase, float dash, float gap, bool skew )
	{
		using var painter = Painter.Begin( target );
		painter.Clear( Color.Transparent );
		painter.Fill = Color.Green;
		painter.Stroke = Stroke.Solid( styled ? Fill.LinearGradient( Color.Red, Color.Blue, 35 ) : Color.White, width )
			.WithCap( cap ) with
		{ Style = style, DashLength = dash, Gap = gap, Offset = phase };
		if ( styled )
		{
			painter.Opacity = 0.6f;
			painter.Clip( new Rect( 12, 8, 35, 42 ) );
			painter.Transform = Matrix.CreateScale( new Vector3( 0.8f, 1.1f, 1 ) );
			if ( skew ) painter.Transform = Matrix.CreateSkewX( 70 ) * Matrix.CreateScale( new Vector3( 0.2f, 1, 1 ) );
		}
		var from = new Vector2( 20 );
		if ( reference )
		{
			painter.Line( [from, from, to] );
		}
		else
		{
			painter.Line( from, to );
		}
	}
}
