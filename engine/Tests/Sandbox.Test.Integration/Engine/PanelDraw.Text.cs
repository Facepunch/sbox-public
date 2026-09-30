using Sandbox.UI;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;

namespace EngineTests;

/// <summary>
/// Checks cached text lifetime and opacity independently of shape drawing.
/// </summary>
[TestClass]
public partial class PanelDrawTextTest : PainterTestBase
{
	[TestMethod]
	public void TextStyleScalesAndReusesLayout()
	{
		WithBuffer( layer =>
		{
			var text = "Styled text";
			var rect = new Rect( 0, 0, 400, 100 );
			PaintTextStyle = new TextStyle
			{
				FontSize = 22,
				FontWeight = 700,
				Italic = true,
				Color = Color.Green.WithAlpha( 0.6f ),
				Alignment = TextFlag.RightBottom,
				LetterSpacing = 2,
				WordSpacing = 3,
				LineHeight = 1.4f
			};
			var settings = PaintTextStyle.CreateScope( text, PaintContext.ScaleToScreen );
			Assert.AreEqual( 44f, settings.FontSize );
			Assert.AreEqual( 4f, settings.LetterSpacing );
			Assert.AreEqual( 6f, settings.WordSpacing );
			var block = TextRendering.GetOrCreateTextBlock( settings, TextFlag.RightBottom, rect.Size );
			Paint.Text( text, rect );
			var first = layer.Instances.ToArray();
			Assert.IsTrue( first.Length > 0 );
			Assert.IsNull( block.Texture );
			Assert.IsTrue( first.All( x => Math.Abs( x.GPU.Color.a - 0.6f ) < 0.001f ) );
			PaintTextStyle = PaintTextStyle with { Color = Color.Green.WithAlpha( 0.2f ) };
			Paint.Text( text, rect );
			Assert.AreSame( block, TextRendering.GetOrCreateTextBlock( PaintTextStyle.CreateScope( text, 2 ), TextFlag.RightBottom, rect.Size ) );
			Assert.AreEqual( first.Length * 2, layer.Instances.Count );
			for ( int i = 0; i < first.Length; i++ )
			{
				Assert.AreEqual( first[i].GPU.Rect, layer.Instances[first.Length + i].GPU.Rect );
				Assert.AreEqual( 0.2f, layer.Instances[first.Length + i].GPU.Color.a, 0.001f );
			}
		} );
	}

	[TestMethod]
	public void TextTransformsReuseLayout()
	{
		WithBuffer( layer =>
		{
			var rect = new Rect( 0, 0, 200, 40 );
			PaintTextStyle = new TextStyle { FontSize = 14, Color = Color.White };
			Paint.Text( "Transformed text", rect );
			var first = layer.Instances.ToArray();
			Paint.Translate( 100, 200 );
			Paint.Rotate( 45 );
			Paint.Scale( 2 );
			Paint.Text( "Transformed text", rect );
			Assert.AreEqual( first.Length * 2, layer.Instances.Count );
			for ( int i = 0; i < first.Length; i++ )
			{
				var moved = layer.Instances[first.Length + i];
				Assert.AreEqual( first[i].GPU.Rect, moved.GPU.Rect );
				Assert.AreEqual( PaintTransform, moved.Transform );
				Assert.AreEqual( Matrix.Identity, first[i].Transform );
			}
		} );
	}

	void WithBuffer( Action<PainterTestOutput> test )
	{
		using var scope = Paint.Scope();
		using var legacy = new LegacyPaint.Binding( PaintContext );
		var buffer = PaintContext;
		using var layer = new PainterTestOutput( buffer.Batcher );
		buffer.State = new();
		buffer.ScaleToScreen = 2;
		buffer.InheritedOpacity = 1;
		try
		{
			test( layer );
		}
		finally
		{
			layer.Clear();
		}
	}

	static ConcurrentDictionary<int, TextRendering.TextBlock> TextCache =>
		(ConcurrentDictionary<int, TextRendering.TextBlock>)typeof( TextRendering )
			.GetField( "Dictionary", BindingFlags.Static | BindingFlags.NonPublic ).GetValue( null );

	static void WithTextBlock( TextRendering.Scope scope, Vector2 clip, Action<TextRendering.TextBlock> test )
	{
		if ( Application.IsHeadless )
			Assert.Inconclusive( "Text caching is unavailable in headless mode." );

		var frame = Application.FrameCount;
		var block = TextRendering.GetOrCreateTextBlock( scope, TextFlag.LeftTop, clip );
		try
		{
			test( block );
		}
		finally
		{
			TextCache.TryRemove( new KeyValuePair<int, TextRendering.TextBlock>( block.CacheKey, block ) );
			block.Dispose();
			Application.FrameCount = frame;
		}
	}

	static void TickTextCache()
	{
		var timer = typeof( TextRendering ).GetField( "_timeSinceCleanup", BindingFlags.Static | BindingFlags.NonPublic );
		var saved = timer.GetValue( null );
		try
		{
			timer.SetValue( null, (RealTimeSince)10f );
			TextRendering.Tick();
		}
		finally
		{
			timer.SetValue( null, saved );
		}
	}

	static void AgeTexture( Texture texture )
	{
		for ( var i = 0; i < 3; i++ )
			NativeEngine.g_pResourceSystem.UpdateSimple();

		Assert.IsTrue( texture.LastUsed > 2, "The texture must be old enough for cache eviction." );
	}

	static void RequireTextureUsageTracking( Texture texture )
	{
		Assert.IsTrue( texture.IsValid );
		// A nonzero mip size bypasses MarkUsed's initial frame-zero deduplication.
		texture.MarkUsed( 1 );
		if ( texture.LastUsed != 0 )
			Assert.Inconclusive( "Requires native texture usage tracking; the empty renderer always reports LastUsed = 1000." );
	}

	/// <summary>
	/// Cache eviction leaves a retained draw instance's texture alive.
	/// </summary>
	[TestMethod]
	public void EvictionRetainsReferencedTexture()
	{
		if ( !Graphics.IsAvailable ) Assert.Inconclusive( "Requires graphics to rasterize text." );

		var scope = new TextRendering.Scope( $"Panel text lifetime {Guid.NewGuid()}", Color.White, 14 );
		WithTextBlock( scope, new Vector2( 300, 60 ), block =>
		{
			block.MakeReady();
			var texture = block.Texture;
			Assert.IsNotNull( texture );
			Assert.IsTrue( texture.IsValid );
			Assert.AreSame( block, TextCache[block.CacheKey] );

			WithBuffer( layer =>
			{
				layer.Batcher.Add( new Painter.BoxDescriptor( new Rect( 0, 0, 300, 60 ), Color.Transparent )
				{
					BackgroundImage = texture,
					BackgroundTint = Color.White,
				} );
				AgeTexture( texture );
				Application.FrameCount += 3;
				TickTextCache();

				Assert.IsFalse( TextCache.ContainsKey( block.CacheKey ), "The entry must actually be evicted." );
				Assert.AreSame( texture, layer.Instances.Single().BackgroundImage );
				Assert.IsTrue( texture.IsValid, "Eviction must not dispose a referenced texture." );
				Assert.AreSame( texture, block.Texture );
			} );
		} );
	}

	/// <summary>
	/// A retained block re-registers its cache key without replacing its raster.
	/// </summary>
	[TestMethod]
	public void RetainedBlockMakeReadyRegistersExistingTexture()
	{
		if ( !Graphics.IsAvailable ) Assert.Inconclusive( "Requires graphics to rasterize text." );

		var scope = new TextRendering.Scope( $"Panel text re-register {Guid.NewGuid()}", Color.White, 14 );
		var clip = new Vector2( 300, 60 );
		WithTextBlock( scope, clip, block =>
		{
			block.MakeReady();
			var texture = block.Texture;
			var key = block.CacheKey;
			Assert.IsNotNull( texture );
			Assert.IsTrue( texture.IsValid );
			Assert.AreSame( block, TextCache[key] );
			AgeTexture( texture );
			Application.FrameCount += 3;
			TickTextCache();
			Assert.IsFalse( TextCache.ContainsKey( key ) );
			Assert.AreSame( texture, block.Texture );
			Assert.IsTrue( texture.IsValid );

			block.MakeReady();

			Assert.AreEqual( key, block.CacheKey );
			Assert.AreSame( block, TextCache[key], "MakeReady must register before returning an existing texture." );
			Assert.AreSame( block, TextRendering.GetOrCreateTextBlock( scope, TextFlag.LeftTop, clip ) );
			Assert.AreSame( texture, block.Texture, "Re-registration must not rasterize another texture." );
			Assert.IsTrue( texture.IsValid );
			Assert.AreEqual( Application.FrameCount, block.LastPreparedFrame );
		} );
	}

	/// <summary>
	/// A recent text request retains an entry even without a texture.
	/// </summary>
	[TestMethod]
	public void RecentTextRequestRetainsCachedBlock()
	{
		var scope = new TextRendering.Scope( $"Panel text recent request {Guid.NewGuid()}", Color.White, 14 );
		WithTextBlock( scope, new Vector2( 300, 60 ), block =>
		{
			Assert.IsNull( block.Texture );
			Application.FrameCount += 2;
			TickTextCache();
			Assert.AreSame( block, TextCache[block.CacheKey] );

			Application.FrameCount++;
			TickTextCache();
			Assert.IsFalse( TextCache.ContainsKey( block.CacheKey ), "An old request without a texture must expire." );
		} );
	}

	/// <summary>
	/// Measuring text refreshes preparation usage without marking its texture rendered.
	/// </summary>
	[TestMethod]
	public void MeasurementRetainsTextureWithoutRendering()
	{
		if ( !Graphics.IsAvailable ) Assert.Inconclusive( "Requires graphics to rasterize text." );

		var scope = new TextRendering.Scope( $"Panel text measurement {Guid.NewGuid()}", Color.White, 14 );
		var clip = new Vector2( 300, 60 );
		WithTextBlock( scope, clip, block =>
		{
			block.MakeReady();
			var texture = block.Texture;
			AgeTexture( texture );
			Application.FrameCount += 3;

			Graphics.MeasureText( new Rect( Vector2.Zero, clip ), scope, TextFlag.LeftTop );
			TickTextCache();
			Assert.AreSame( block, TextCache[block.CacheKey], "Measurement must refresh cache usage without rendering." );
			Assert.AreSame( texture, block.Texture );
			Assert.IsTrue( texture.LastUsed > 2, "Measurement must not pretend the GPU used this texture." );

			Application.FrameCount += 3;
			TickTextCache();
			Assert.IsFalse( TextCache.ContainsKey( block.CacheKey ) );
			Assert.IsTrue( texture.IsValid );
		} );
	}

	/// <summary>
	/// Native texture use retains old requests through two resource frames, but not three.
	/// </summary>
	[TestMethod]
	public void RecentTextureUseRetainsCachedBlock()
	{
		if ( !Graphics.IsAvailable ) Assert.Inconclusive( "Requires graphics to rasterize text." );

		var scope = new TextRendering.Scope( $"Panel text recent texture {Guid.NewGuid()}", Color.White, 14 );
		WithTextBlock( scope, new Vector2( 300, 60 ), block =>
		{
			block.MakeReady();
			var texture = block.Texture;
			Assert.IsNotNull( texture );
			RequireTextureUsageTracking( texture );
			Application.FrameCount += 3;
			NativeEngine.g_pResourceSystem.UpdateSimple();
			NativeEngine.g_pResourceSystem.UpdateSimple();
			Assert.AreEqual( 2, texture.LastUsed );

			TickTextCache();
			Assert.AreSame( block, TextCache[block.CacheKey] );
			Assert.AreSame( texture, block.Texture );

			NativeEngine.g_pResourceSystem.UpdateSimple();
			Assert.AreEqual( 3, texture.LastUsed );
			TickTextCache();
			Assert.IsFalse( TextCache.ContainsKey( block.CacheKey ) );
			Assert.IsTrue( texture.IsValid );
		} );
	}

	/// <summary>
	/// Static images are marked used without applying video visibility or pause rules.
	/// </summary>
	[TestMethod]
	public void RendererMarkPresentedUpdatesStaticTextureUsage()
	{
		using var texture = Texture.Create( 1, 1 ).WithName( $"Panel static texture {Guid.NewGuid()}" )
			.WithData( new byte[] { 255, 255, 255, 255 } ).WithStaticUsage().Finish();
		RequireTextureUsageTracking( texture );
		AgeTexture( texture );
		Assert.IsFalse( texture.IsAnimated );
		var panel = new Panel();
		panel.Box.Rect = new Rect( 300, 0, 100, 100 );
		var scissor = Painter.Scissoring.Single( new Rect( 0, 0, 200, 200 ), BorderRadii.Zero, Matrix.Identity );
		var frame = Application.FrameCount;
		bool? onScreen = null;
		try
		{
			Application.FrameCount++;
			PainterBatcher.MarkPresented( texture, panel.Box.Rect, Matrix.Identity, scissor, ref onScreen, playbackPaused: true );
			Assert.AreEqual( 0, texture.LastUsed, "Presentation must touch a non-video static texture." );
			Assert.IsNull( onScreen );
		}
		finally
		{
			Application.FrameCount = frame;
			panel.Delete( true );
		}
	}

	/// <summary>
	/// Draw instances store ordinary images directly and leave untextured shapes empty.
	/// </summary>
	[TestMethod]
	public void BackgroundImageIsStoredDirectly()
	{
		WithBuffer( layer =>
		{
			layer.Batcher.Add( new Painter.BoxDescriptor( new Rect( 0, 0, 100, 20 ), Color.White ) { BackgroundImage = Texture.White } );
			layer.Batcher.Add( new Painter.BoxDescriptor( new Rect( 0, 0, 100, 20 ), Color.White ) );
			Assert.AreSame( Texture.White, layer.Instances[0].BackgroundImage );
			Assert.IsNull( layer.Instances[1].BackgroundImage );
		} );
	}

	/// <summary>
	/// Alpha changes reuse the same layout and multiply the glyph opacity.
	/// </summary>
	[TestMethod]
	public void TextAlphaReusesCachedBlock()
	{
		var text = $"Panel text alpha {Guid.NewGuid()}";
		var rect = new Rect( 10, 20, 300.25f, 60.5f );
		var color = new Color( 0.2f, 0.4f, 0.6f );
		const float size = 14.25f;
		var scope = new TextRendering.Scope( text, color.WithAlpha( 1 ), size * 2 );
		WithTextBlock( scope, rect.Size, textBlock =>
		{
			textBlock.EnsureLayout();
			Assert.IsNull( textBlock.Texture );

			WithBuffer( layer =>
			{
				foreach ( var opacity in new[] { 1f, 0.5f, 0f } )
					foreach ( var alpha in new[] { 0f, 0.25f, 0.6f, 1f } )
					{
						layer.Clear();
						PaintContext.InheritedOpacity = opacity;
						Application.FrameCount += 3;
						var lastPreparedFrame = textBlock.LastPreparedFrame;
						PaintTextStyle = new TextStyle { FontSize = size, Color = color.WithAlpha( alpha ) };
						Paint.Text( text, rect );
						if ( alpha * opacity == 0 )
						{
							Assert.AreEqual( 0, layer.Instances.Count, "Fully transparent text must not emit glyphs." );
							Assert.AreEqual( lastPreparedFrame, textBlock.LastPreparedFrame, "Skipped text must not refresh cache usage." );
							Assert.IsNull( textBlock.Texture );
							continue;
						}

						Assert.IsTrue( layer.Instances.Count > 0 );
						Assert.IsNull( textBlock.Texture );
						Assert.AreEqual( Application.FrameCount, textBlock.LastPreparedFrame );
						Assert.AreSame( textBlock, TextCache[textBlock.CacheKey] );
						Assert.AreSame( textBlock, TextRendering.GetOrCreateTextBlock( scope, TextFlag.LeftTop, rect.Size ) );
						Assert.IsTrue( layer.Instances.All( x => Math.Abs( x.GPU.Color.a - alpha * opacity ) < 0.001f ) );
					}
			} );
		} );
	}

	/// <summary>
	/// Headless text drawing emits nothing when the text cache is unavailable.
	/// </summary>
	[TestMethod]
	public void HeadlessTextDrawIsEmpty()
	{
		if ( !Application.IsHeadless )
			Assert.Inconclusive( "Requires headless text rendering." );

		WithBuffer( layer =>
		{
			PaintTextStyle = new TextStyle { FontSize = 14, Color = Color.White };
			Paint.Text( "Headless", new Rect( 0, 0, 100, 20 ) );
			Assert.IsTrue( layer.Instances.Count == 0 );
		} );
	}
}
