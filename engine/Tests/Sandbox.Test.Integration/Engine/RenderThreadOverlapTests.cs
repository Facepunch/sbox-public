using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace EngineTests;

/// <summary>
/// The main thread's use of a Borealis.Gpu device while the render thread draws a frame on it
/// (docs/reference/borealis-render-thread.md): what the game writes and paints in its update is kept and done once the frame
/// is drawn, in order, instead of waiting for the drawing.
/// </summary>
[TestClass]
[DoNotParallelize]
public class RenderThreadOverlapTests
{
	// Longer than a test takes without waiting: a use that waits for the drawing waits until this lets it go
	const int ReleaseMs = 3000;

	static byte[] Pixels( int width, int height, Color32 color )
	{
		var pixels = new Color32[width * height];
		pixels.AsSpan().Fill( color );
		return MemoryMarshal.AsBytes( pixels.AsSpan() ).ToArray();
	}

	/// <summary>
	/// A texture updated while the render thread draws is kept and written after the frame, without waiting for it. Before,
	/// the update waited for the drawing (a startup screen's progress texture waited 104 ms).
	/// </summary>
	[TestMethod]
	public void TextureUpdatesDontWaitForTheDrawing()
	{
		TestGraphics.Require();
		ThreadSafe.MarkMainThread();
		using var borealis = BorealisGpuRenderBackend.Create( validation: true );

		const int size = 16;
		using var texture = Texture.Create( size, size, ImageFormat.RGBA8888 ).WithData( Pixels( size, size, new Color32( 0, 0, 255, 255 ) ) ).Finish();
		Assert.AreEqual( new Color32( 0, 0, 255, 255 ), borealis.ReadPixels( texture )[0], "the copy is made" );

		borealis.DrawingForTests( true );
		using var release = new Timer( _ => borealis.DrawingForTests( false ), null, ReleaseMs, Timeout.Infinite );
		var timer = Stopwatch.StartNew();
		texture.Update( Pixels( size, size, new Color32( 255, 0, 0, 255 ) ), 0, 0, size, size );
		var took = timer.ElapsedMilliseconds;
		var staged = borealis.TextureUpdatesStaged;
		borealis.DrawingForTests( false );

		Assert.IsTrue( took < ReleaseMs / 2, $"the update didn't wait for the drawing ({took} ms)" );
		Assert.AreEqual( 1, staged, "kept for after the frame" );
		Assert.AreEqual( new Color32( 255, 0, 0, 255 ), borealis.ReadPixels( texture )[size * size / 2], "written once the frame was drawn" );
		Assert.AreEqual( 0, borealis.ValidationErrors );
	}

	/// <summary>
	/// The game updates a texture during the drawing (the physgun's screen graph) while the render thread releases another
	/// (a disposed stage texture, <c>BorealisGpuFrame.DropDisposedStageTextures</c> through <c>ReleaseEverywhere</c>) and
	/// finishes its frame only after that. Before, the main thread held the devices' list lock while its update waited for
	/// the drawing, the render thread waited for that lock, and both stopped.
	/// </summary>
	[TestMethod]
	public void TextureUpdatesDuringTheDrawingDontDeadlockWithItsReleases()
	{
		TestGraphics.Require();
		ThreadSafe.MarkMainThread();
		using var borealis = BorealisGpuRenderBackend.Create( validation: true );

		const int size = 16;
		using var texture = Texture.Create( size, size, ImageFormat.RGBA8888 ).WithData( Pixels( size, size, new Color32( 0, 0, 255, 255 ) ) ).Finish();
		using var released = Texture.Create( size, size, ImageFormat.RGBA8888 ).WithData( Pixels( size, size, new Color32( 0, 255, 0, 255 ) ) ).Finish();
		borealis.ReadPixels( texture );
		borealis.ReadPixels( released );

		borealis.DrawingForTests( true );

		// Never left drawing: a deadlock is let go after the release time, and shows as the time it took
		using var release = new Timer( _ => borealis.DrawingForTests( false ), null, ReleaseMs, Timeout.Infinite );
		var renderThread = new Thread( () =>
		{
			ThreadSafe.MarkRenderThread();
			Thread.Sleep( 200 );
			RenderBackend.ReleaseEverywhere( released );
			borealis.DrawingForTests( false );
		} );

		var timer = Stopwatch.StartNew();
		renderThread.Start();
		for ( int i = 0; i < 20; i++ )
		{
			texture.Update( Pixels( size, size, new Color32( (byte)(i * 10), 0, 0, 255 ) ), 0, 0, size, size );
			Thread.Sleep( 20 );
		}

		Assert.IsTrue( renderThread.Join( ReleaseMs * 2 ), "the render thread's frame ended" );
		var took = timer.ElapsedMilliseconds;
		Assert.IsTrue( took < ReleaseMs / 2, $"neither thread waited on the other ({took} ms)" );
		Assert.AreEqual( new Color32( 190, 0, 0, 255 ), borealis.ReadPixels( texture )[0], "the last update landed after the frame" );
		Assert.AreEqual( 0, borealis.ValidationErrors );
	}

	/// <summary>
	/// A texture first used on the main thread while the render thread draws (a panel's new background image) is made as a
	/// worker makes one, in a list of its own, without waiting for the drawing. Before, making it waited.
	/// </summary>
	[TestMethod]
	public void TexturesMadeWhileTheRenderThreadDrawsDontWait()
	{
		TestGraphics.Require();
		ThreadSafe.MarkMainThread();
		using var borealis = BorealisGpuRenderBackend.Create( validation: true );

		const int size = 16;
		using var texture = Texture.Create( size, size, ImageFormat.RGBA8888 ).WithData( Pixels( size, size, new Color32( 0, 255, 0, 255 ) ) ).Finish();

		borealis.DrawingForTests( true );
		using var release = new Timer( _ => borealis.DrawingForTests( false ), null, ReleaseMs, Timeout.Infinite );
		var timer = Stopwatch.StartNew();
		var index = borealis.TextureIndex( texture );
		var took = timer.ElapsedMilliseconds;
		borealis.DrawingForTests( false );

		Assert.IsTrue( took < ReleaseMs / 2, $"making it didn't wait for the drawing ({took} ms)" );
		Assert.IsTrue( index > 0, "it has a bindless index" );
		Assert.AreEqual( new Color32( 0, 255, 0, 255 ), borealis.ReadPixels( texture )[0], "made with its pixels" );
		Assert.AreEqual( 0, borealis.ValidationErrors );
	}

	/// <summary>
	/// A texture made while the render thread draws and released before the main thread next records (a web panel's texture,
	/// made again as its page resizes) isn't tracked once it's gone. Before, its layout, kept for the main thread's next
	/// recording, was tracked after the release had forgotten it, and while a worker's clear was queued unreadable the end of
	/// every block after threw on the destroyed texture, so nothing drew again (DXRP's menu, 25 Nov).
	/// </summary>
	[TestMethod]
	public void TexturesReleasedBeforeTheMainThreadRecordsArentTracked()
	{
		TestGraphics.Require();
		ThreadSafe.MarkMainThread();
		using var borealis = BorealisGpuRenderBackend.Create( validation: true );

		// Made with no pixels, as a web panel's is: cleared, so left for a write rather than readable
		const int size = 16;
		using var texture = Texture.Create( size, size, ImageFormat.BGRA8888 ).Finish();

		borealis.DrawingForTests( true );
		Assert.IsTrue( borealis.TextureIndex( texture ) > 0, "made while the render thread draws" );
		borealis.DrawingForTests( false );
		borealis.Release( texture );

		borealis.BeginBlock();
		borealis.EndBlock();
		borealis.BeginBlock();
		borealis.EndBlock();

		Assert.IsTrue( borealis.TextureIndex( texture ) > 0, "made again when next used" );
		Assert.AreEqual( 0, borealis.ValidationErrors );
	}

	/// <summary>
	/// A texture made with no pixels, written while the render thread draws and drawn before that write lands (a web panel's
	/// page: made and written as the page paints, then recorded by its panel) has the write in a copy of its own format and
	/// size once it lands. Before, the update had cleared the colour the copy would be made with, so the copy was the
	/// transparent pixel, the write didn't fit it and was dropped, and the page never showed (DXRP's patch notes, 25 Nov).
	/// </summary>
	[TestMethod]
	public void TexturesWrittenWhileTheRenderThreadDrawsTakeTheWrite()
	{
		TestGraphics.Require();
		ThreadSafe.MarkMainThread();
		using var borealis = BorealisGpuRenderBackend.Create( validation: true );

		const int size = 16;
		using var texture = Texture.Create( size, size, ImageFormat.BGRA8888 ).Finish();

		borealis.DrawingForTests( true );
		texture.Update( Pixels( size, size, new Color32( 255, 0, 255, 255 ) ), 0, 0, size, size );
		Assert.IsTrue( borealis.TextureIndex( texture ) > 0, "drawn before the write lands" );
		borealis.DrawingForTests( false );

		var bytes = borealis.ReadBytes( texture );
		Assert.AreEqual( size * size * 4, bytes.Length, "a copy of its own size" );
		var middle = (8 * size + 8) * 4;
		CollectionAssert.AreEqual( new byte[] { 255, 0, 255, 255 }, bytes[middle..(middle + 4)], "the write is in it" );
		Assert.AreEqual( 0, borealis.ValidationErrors );
	}

	/// <summary>
	/// A render target first used while the render thread draws is cleared in a list of its own and queued for the context
	/// once, readable, as the clear leaves it. Before, it was queued twice (undefined, then where the clear left it): the
	/// render thread, which takes queued layouts as each block begins, took the first in one block and the second in the
	/// next, and moved the target from a layout it had already left (the sandbox HUD's target as the spawn menu opened).
	/// </summary>
	[TestMethod]
	public void TargetsMadeWhileTheRenderThreadDrawsAreQueuedOnceReadable()
	{
		TestGraphics.Require();
		ThreadSafe.MarkMainThread();
		using var borealis = BorealisGpuRenderBackend.Create( validation: true );

		const int size = 16;
		using var target = Texture.CreateRenderTarget().WithSize( size, size ).WithFormat( ImageFormat.RGBA8888 ).WithInitialColor( Color.Green ).Create();

		borealis.DrawingForTests( true );
		using var release = new Timer( _ => borealis.DrawingForTests( false ), null, ReleaseMs, Timeout.Infinite );
		var index = borealis.TextureIndex( target );
		var queued = borealis.QueuedLayoutsForTests;
		borealis.DrawingForTests( false );

		Assert.IsTrue( index > 0, "it has a bindless index" );
		CollectionAssert.AreEqual( new[] { Borealis.Gpu.ImageLayout.ShaderRead }, queued, "queued once, readable" );
		Assert.AreEqual( new Color32( 0, 255, 0, 255 ), borealis.ReadPixels( target )[0], "cleared" );
		Assert.AreEqual( 0, borealis.ValidationErrors );
	}

	/// <summary>
	/// Painting into a texture while the render thread draws runs once the frame is drawn, after a buffer write made before it
	/// and before an update made after it. Before, it waited for the drawing.
	/// </summary>
	[TestMethod]
	public void PaintingIntoATextureRunsAfterTheDrawingInOrder()
	{
		TestGraphics.Require();
		ThreadSafe.MarkMainThread();
		using var borealis = BorealisGpuRenderBackend.Create( validation: true );

		const int size = 32;
		using var target = Texture.CreateRenderTarget().WithSize( size, size ).Create();
		using ( var painter = Painter.Begin( target, borealis ) ) painter.Clear( Color.Blue );
		Assert.AreEqual( new Color32( 0, 0, 255, 255 ), borealis.ReadPixels( target )[0], "painted before" );

		borealis.DrawingForTests( true );
		using var release = new Timer( _ => borealis.DrawingForTests( false ), null, ReleaseMs, Timeout.Infinite );
		var timer = Stopwatch.StartNew();
		using ( var painter = Painter.Begin( target, borealis ) )
		{
			painter.Clear( Color.Transparent );
			painter.Fill = Color.Red;
			painter.Rect( new Rect( 0, 0, size, size ) );
		}

		var took = timer.ElapsedMilliseconds;
		var deferred = borealis.UpdateWorkDeferred;
		borealis.DrawingForTests( false );

		Assert.IsTrue( took < ReleaseMs / 2, $"the painting didn't wait for the drawing ({took} ms)" );
		Assert.AreEqual( 1, deferred, "run after the frame" );
		Assert.AreEqual( new Color32( 255, 0, 0, 255 ), borealis.ReadPixels( target )[size * size / 2 + size / 2], "painted once the frame was drawn" );
		Assert.AreEqual( 0, borealis.ValidationErrors );
	}
}
