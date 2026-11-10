using Sandbox.Rendering;
using System;

namespace EngineTests;

[TestClass]
public class PainterDestinationsTest
{
	static readonly Rect Bounds = new( 0, 0, 64, 64 );

	[TestMethod]
	public void RingFillsBandAndStrokesBothEdges()
	{
		RequireVulkan();
		using var target = Texture.CreateRenderTarget().WithSize( 64, 64 ).Create();
		using ( var painter = Painter.Begin( target ) )
		{
			painter.Clear( Color.Transparent );
			painter.Fill = Color.Red;
			painter.Stroke = new Stroke( Color.Blue, 4 );
			painter.Ring( Bounds.Center, innerRadius: 10, outerRadius: 24 );
		}
		using var bitmap = target.GetBitmap();
		AssertColor( Color.Transparent, bitmap.GetPixel( 32, 32 ) );
		AssertColor( Color.Blue, bitmap.GetPixel( 42, 32 ) );
		AssertColor( Color.Red, bitmap.GetPixel( 49, 32 ) );
		AssertColor( Color.Blue, bitmap.GetPixel( 56, 32 ) );
		AssertColor( Color.Transparent, bitmap.GetPixel( 61, 32 ) );
	}

	[TestMethod]
	public void RingImageFillMapsAcrossOuterBounds()
	{
		RequireVulkan();
		using var source = Texture.CreateRenderTarget().WithSize( 64, 64 ).Create();
		using ( var painter = Painter.Begin( source ) )
		{
			painter.Clear( Color.Red );
			painter.Fill = Color.Blue;
			painter.Rect( new Rect( 32, 0, 32, 64 ) );
		}
		using var target = Texture.CreateRenderTarget().WithSize( 64, 64 ).Create();
		using ( var painter = Painter.Begin( target ) )
		{
			painter.Clear( Color.Transparent );
			painter.Fill = Fill.Image( source );
			painter.Ring( Bounds.Center, 10, 24 );
		}
		using var bitmap = target.GetBitmap();
		AssertColor( Color.Red, bitmap.GetPixel( 15, 32 ) );
		AssertColor( Color.Blue, bitmap.GetPixel( 49, 32 ) );
		AssertColor( Color.Transparent, bitmap.GetPixel( 32, 32 ) );
	}

	[TestMethod]
	public void TextureAndCommandListOutputMatch()
	{
		RequireVulkan();
		using var texture = Texture.CreateRenderTarget().WithSize( 64, 64 ).Create();
		using ( var painter = Painter.Begin( texture ) )
		{
			painter.Clear( Color.Transparent );
			DrawExample( painter );
		}

		var commands = new CommandList();
		using ( var painter = Painter.Begin( commands, Bounds ) )
		{
			painter.Clear( Color.Transparent );
			DrawExample( painter );
		}

		using var expected = texture.GetBitmap();
		for ( int playback = 0; playback < 2; playback++ )
		{
			using var target = Render( commands );
			using var actual = target.GetBitmap();
			CollectionAssert.AreEqual( expected.GetPixels(), actual.GetPixels() );
		}
		commands.Reset();
	}

	[TestMethod]
	public void PaintBlocksPreserveCommandOrder()
	{
		RequireVulkan();
		var commands = new CommandList();
		using ( var painter = Painter.Begin( commands, Bounds ) )
		{
			painter.Clear( Color.Transparent );
			painter.Fill = Color.Red;
			painter.Rect( Bounds );
		}

		commands.Clear( Color.Blue );
		using ( var painter = Painter.Begin( commands, Bounds ) )
		{
			painter.Fill = Color.Green;
			painter.Rect( new Rect( 0, 0, 32, 64 ) );
		}

		using var target = Render( commands );
		using var bitmap = target.GetBitmap();
		Assert.AreEqual( Color.Green, bitmap.GetPixel( 16, 32 ) );
		Assert.AreEqual( Color.Blue, bitmap.GetPixel( 48, 32 ) );
		commands.Reset();
	}

	[TestMethod]
	public void TextureRecordingsAreIndependent()
	{
		RequireVulkan();
		using var texture = Texture.CreateRenderTarget().WithSize( 64, 64 ).Create();
		var first = Painter.Begin( texture );
		var context = first.ActiveContext;
		var commands = context.Batcher;
		first.Clear( Color.Red );

		using ( var second = Painter.Begin( texture ) )
		{
			Assert.AreNotSame( context, second.ActiveContext );
			second.Clear( Color.Green );
		}

		using ( var intermediate = texture.GetBitmap() )
		{
			Assert.AreEqual( Color.Green, intermediate.GetPixel( 32, 32 ) );
		}

		first.Fill = Color.Blue;
		first.Rect( new Rect( 0, 0, 32, 64 ) );
		first.Dispose();
		Assert.IsFalse( context.IsPainting );
		Assert.AreEqual( 0, context.CommandList.GetCheckpoint() );
		Assert.AreEqual( 0, commands.Instances.Count );

		using var bitmap = texture.GetBitmap();
		Assert.AreEqual( Color.Blue, bitmap.GetPixel( 16, 32 ) );
		Assert.AreEqual( Color.Red, bitmap.GetPixel( 48, 32 ) );
	}

	[TestMethod]
	public void DisposedTextureReleasesRecordedReferences()
	{
		RequireVulkan();
		var texture = Texture.CreateRenderTarget().WithSize( 64, 64 ).Create();
		var painter = Painter.Begin( texture );
		painter.Texture( Texture.White, Bounds );
		var context = painter.ActiveContext;
		var buffer = context.Batcher;
		texture.Dispose();

		bool rejected = false;
		try
		{
			painter.Dispose();
		}
		catch ( ObjectDisposedException )
		{
			rejected = true;
		}

		Assert.IsTrue( rejected );
		Assert.AreEqual( 0, context.CommandList.GetCheckpoint() );
		Assert.AreEqual( 0, buffer.Instances.Count );
	}

	static void DrawExample( Painter painter )
	{
		painter.Fill = Fill.LinearGradient( Color.Red, Color.Blue );
		painter.Rect( Bounds, 8 );
		using var scope = painter.Scope();
		painter.Clip( Bounds.Shrink( 8 ), 4 );
		painter.Translate( new Vector2( 4, 4 ) );
		painter.Opacity = 0.5f;
		painter.Fill = Color.White;
		painter.Circle( Bounds.Center, 18 );
		using var layer = painter.BeginLayer( new Rect( 12, 12, 24, 24 ), opacity: 0.5f );
		painter.Fill = Color.Green;
		painter.Rect( painter.Bounds, 4 );
	}

	[TestMethod]
	public void CommandListsKeepTheirGpuTables()
	{
		RequireVulkan();
		var first = new CommandList();
		using ( var painter = Painter.Begin( first, Bounds ) )
		{
			painter.Clear( Color.Transparent );
			DrawExample( painter );
		}
		using var reference = Render( first );
		using var expected = reference.GetBitmap();
		var other = new CommandList();
		for ( int frame = 0; frame < 8; frame++ )
		{
			other.Reset();
			using ( var painter = Painter.Begin( other, Bounds ) )
			{
				painter.Clear( Color.Red );
				for ( int i = 0; i < 90; i++ )
				{
					using var scope = painter.Scope();
					painter.Translate( i + frame, i * 0.3f );
					painter.Clip( Bounds.Shrink( i * 0.1f ), i * 0.2f );
					painter.Fill = Fill.LinearGradient( Color.Green, Color.Cyan );
					painter.Rect( Bounds, i * 0.1f );
				}
			}
			using var discarded = Render( other );
			using var replay = Render( first );
			using var actual = replay.GetBitmap();
			CollectionAssert.AreEqual( expected.GetBuffer().ToArray(), actual.GetBuffer().ToArray() );
		}
		other.Reset();
		first.Reset();
	}

	[TestMethod]
	public void AbandonedPaintingRemovesSubmittedCommands()
	{
		RequireVulkan();
		var commands = new CommandList();
		using ( var painter = Painter.Begin( commands, Bounds ) )
		{
			painter.Clear( Color.Green );
		}

		var abandoned = Painter.Begin( commands, Bounds );
		abandoned.Fill = Color.Blue;
		abandoned.Rect( Bounds );
		abandoned.Clear( Color.Yellow );
		using ( var next = Painter.Begin( commands, Bounds ) )
		{
			next.Fill = Color.Red;
			next.Rect( new Rect( 0, 0, 32, 64 ) );
		}
		abandoned.Dispose();
		using var target = Render( commands );
		using var bitmap = target.GetBitmap();
		Assert.AreEqual( Color.Red, bitmap.GetPixel( 16, 32 ) );
		Assert.AreEqual( Color.Green, bitmap.GetPixel( 48, 32 ) );
		commands.Reset();
	}

	[TestMethod]
	public void ReplayingKeepsTextureUsageCurrent()
	{
		RequireVulkan();
		using var texture = Texture.Create( 1, 1 ).WithName( $"Painter usage {Guid.NewGuid()}" )
			.WithData( new byte[] { 255, 255, 255, 255 } ).WithStaticUsage().Finish();
		var commands = new CommandList();
		using ( var painter = Painter.Begin( commands, Bounds ) )
		{
			painter.Texture( texture, Bounds );
		}
		var frame = Application.FrameCount;
		try
		{
			Application.FrameCount += 3;
			for ( int i = 0; i < 3; i++ ) NativeEngine.g_pResourceSystem.UpdateSimple();
			Assert.IsTrue( texture.LastUsed > 0 );
			using var target = Render( commands );
			Assert.AreEqual( 0, texture.LastUsed );
		}
		finally
		{
			Application.FrameCount = frame;
			commands.Reset();
		}
	}

	static Texture Render( CommandList commands )
	{
		var target = Texture.CreateRenderTarget().WithSize( 64, 64 ).Create();
		target.Flags |= TextureFlags.PremultipliedAlpha;
		using ( var scope = Graphics.Scope.Create() )
		{
			scope.Attributes.Set( "UIGammaOutput", true );
			Graphics.RenderTarget = RenderTarget.From( target );
			scope.Context.SetViewport( Bounds );
			commands.Execute();
		}
		return target;
	}

	[TestMethod]
	public void NestedLayersApplyGroupOpacityAndParentClips()
	{
		RequireVulkan();
		var commands = new CommandList();
		using ( var painter = Painter.Begin( commands, Bounds ) )
		{
			painter.Clear( Color.Transparent );
			painter.Translate( 8, 4 );
			for ( int i = 0; i < 6; i++ ) painter.Clip( new Rect( 0, 0, 24, 24 ) );
			painter.Opacity = 0.5f;
			var batcher = painter.ActiveContext.Batcher;
			using ( painter.BeginLayer( new Rect( 0, 0, 32, 32 ), 0.5f ) )
			{
				painter.Fill = Color.Red;
				painter.Rect( painter.Bounds );
				painter.Rect( painter.Bounds );
				using ( painter.BeginLayer( new Rect( 8, 8, 16, 16 ), 0.5f ) )
				{
					Assert.AreSame( batcher, painter.ActiveContext.Batcher );
					painter.Fill = Color.Blue;
					painter.Rect( painter.Bounds );
				}
			}
			painter.Opacity = 1;
			painter.Fill = Color.Green;
			painter.Rect( new Rect( 0, 0, 4, 4 ) );
		}

		using var target = Render( commands );
		using var bitmap = target.GetBitmap();
		AssertColor( Color.Red.WithAlpha( 0.25f ), bitmap.GetPixel( 14, 10 ) );
		AssertColor( new Color( 0.5f, 0, 0.5f, 0.25f ), bitmap.GetPixel( 22, 18 ) );
		AssertColor( Color.Green, bitmap.GetPixel( 10, 6 ) );
		AssertColor( Color.Transparent, bitmap.GetPixel( 36, 20 ) );
		AssertColor( Color.Transparent, bitmap.GetPixel( 4, 4 ) );
		commands.Reset();
	}

	[TestMethod]
	public void LayerFiltersMasksAndBackdropsPreserveOrder()
	{
		RequireVulkan();
		using var target = Texture.CreateRenderTarget().WithSize( 64, 64 ).Create();
		using ( var painter = Painter.Begin( target ) )
		{
			painter.Clear( Color.Blue );
			painter.Translate( 8, 8 );
			using ( painter.BeginLayer( new Rect( 0, 0, 40, 40 ), filter: new Painter.Filter { Saturation = 0 },
				mask: new Painter.Mask( Texture.White, new Rect( 0, 0, 32, 40 ), Repeat: Sandbox.UI.BackgroundRepeat.NoRepeat, Sampling: FilterMode.Point ) ) )
			{
				painter.Fill = Color.Red;
				painter.Rect( painter.Bounds );
				painter.FilterBackdrop( new Rect( 20, 0, 20, 40 ), new Painter.Filter { Invert = 1 } );
			}
			painter.Fill = Color.Green;
			painter.Rect( new Rect( 0, 0, 4, 4 ) );
		}
		using var bitmap = target.GetBitmap();
		AssertColor( new Color( 0.213f, 0.213f, 0.213f ), bitmap.GetPixel( 18, 28 ) );
		AssertColor( new Color( 0.787f, 0.787f, 0.787f ), bitmap.GetPixel( 34, 28 ) );
		AssertColor( Color.Blue, bitmap.GetPixel( 44, 28 ) );
		AssertColor( Color.Green, bitmap.GetPixel( 10, 10 ) );
	}

	[TestMethod]
	public void AbandonedLayersDoNotChangeTheNextRecording()
	{
		RequireVulkan();
		var commands = new CommandList();
		using ( var painter = Painter.Begin( commands, Bounds ) ) painter.Clear( Color.Green );
		var abandoned = Painter.Begin( commands, Bounds );
		var layer = abandoned.BeginLayer( Bounds );
		abandoned.Fill = Color.Blue;
		abandoned.Rect( Bounds );
		using ( var next = Painter.Begin( commands, Bounds ) )
		{
			layer.Dispose();
			abandoned.Dispose();
			next.Fill = Color.Red;
			next.Rect( new Rect( 0, 0, 32, 64 ) );
		}
		using var target = Render( commands );
		using var bitmap = target.GetBitmap();
		AssertColor( Color.Red, bitmap.GetPixel( 16, 32 ) );
		AssertColor( Color.Green, bitmap.GetPixel( 48, 32 ) );
		commands.Reset();
	}

	static void AssertColor( Color expected, Color actual )
	{
		const float tolerance = 0.02f;
		Assert.AreEqual( expected.r, actual.r, tolerance, "Red" );
		Assert.AreEqual( expected.g, actual.g, tolerance, "Green" );
		Assert.AreEqual( expected.b, actual.b, tolerance, "Blue" );
		Assert.AreEqual( expected.a, actual.a, tolerance, "Alpha" );
	}

	[TestMethod]
	[DataRow( BlendMode.Normal, 0.45f, 0.25f, 0.45f, 1f )]
	[DataRow( BlendMode.Multiply, 0.16f, 0.12f, 0.36f, 0.5f )]
	[DataRow( BlendMode.Lighten, 0.5f, 0.35f, 0.6f, 1f )]
	[DataRow( BlendMode.PremultipliedAlpha, 0.45f, 0.25f, 0.45f, 1f )]
	public void LayerCompositingUsesDestinationBlendMode( BlendMode blend, float red, float green, float blue, float alpha )
	{
		RequireVulkan();
		using var target = Texture.CreateRenderTarget().WithSize( 64, 64 ).Create();
		using ( var painter = Painter.Begin( target ) )
		{
			painter.Clear( new Color( 0.1f, 0.2f, 0.3f ) );
			painter.BlendMode = blend;
			using var layer = painter.BeginLayer( Bounds, 0.5f );
			painter.Fill = new Color( 0.8f, 0.3f, 0.6f );
			painter.Rect( Bounds );
		}
		using var bitmap = target.GetBitmap();
		// GetBitmap returns straight alpha, including the multiply blend's 0.5 output alpha.
		AssertColor( new Color( red, green, blue, alpha ), bitmap.GetPixel( 32, 32 ) );
	}

	[TestMethod]
	public void StandaloneCommandsSampleNewTexturesAcrossSubmissions()
	{
		RequireVulkan();
		var sources = new System.Collections.Generic.List<Texture>();
		try
		{
			// Keep textures alive so descriptor-slot reuse cannot hide a stale buffered set.
			for ( int i = 0; i < 16; i++ )
			{
				var color = new Color( (i & 1) == 0 ? 1 : 0, (i & 2) == 0 ? 1 : 0, (i & 4) == 0 ? 1 : 0 );
				var source = Texture.CreateRenderTarget().WithSize( 64, 64 ).Create();
				sources.Add( source );
				using ( var painter = Painter.Begin( source ) ) painter.Clear( color );

				var commands = new CommandList();
				try
				{
					using ( var painter = Painter.Begin( commands, Bounds ) ) painter.Texture( source, Bounds );
					using var target = Render( commands );
					using var bitmap = target.GetBitmap();
					AssertColor( color, bitmap.GetPixel( 32, 32 ) );
				}
				finally
				{
					commands.Reset();
				}
			}
		}
		finally
		{
			foreach ( var source in sources ) source.Dispose();
		}
	}

	static void RequireVulkan()
	{
		if ( g_pRenderDevice.GetRenderDeviceAPI() != NativeEngine.RenderDeviceAPI_t.RENDER_DEVICE_API_VULKAN )
			Assert.Inconclusive( "Requires Vulkan rendering." );
	}
}
