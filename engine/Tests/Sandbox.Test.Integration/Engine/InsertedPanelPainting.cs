using Sandbox.Rendering;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace EngineTests;

[TestClass]
public class InsertedPanelPaintingTest
{
	[TestMethod]
	public void InsertedPanelsRefreshTheirBuffersAfterRecordingAgain()
	{
		TestGraphics.Require();

		var bounds = new Rect( 0, 0, 64, 64 );
		var panels = new CommandList();
		var frame = new CommandList();
		frame.InsertList( panels );
		using var target = Texture.CreateRenderTarget().WithSize( 64, 64 ).Create();
		try
		{
			foreach ( var color in new[] { Color.Red, Color.Blue, Color.Green } )
			{
				panels.Reset();
				using ( var painter = Painter.Begin( panels, bounds ) )
				{
					painter.Clear( Color.Transparent );
					painter.Fill = color;
					painter.Rect( bounds );
				}
				using ( var scope = Graphics.Scope.Create() )
				{
					scope.Attributes.Set( "UIGammaOutput", true );
					Graphics.RenderTarget = RenderTarget.From( target );
					Graphics.Viewport = bounds;
					frame.Execute();
				}
				using var bitmap = target.GetBitmap();
				Assert.AreEqual( color, bitmap.GetPixel( 32, 32 ), "An inserted list must upload the newly recorded panels." );
			}
		}
		finally
		{
			frame.Reset();
			panels.Reset();
		}
	}

	[TestMethod]
	public void RecursiveInsertionIsRejectedAndBothListsCanReplayAgain()
	{
		var outer = new CommandList( "outer" );
		var inner = new CommandList( "inner" );
		outer.InsertList( inner );
		inner.InsertList( outer );

		Assert.ThrowsException<InvalidOperationException>( outer.Execute );

		int runs = 0;
		inner.Reset();
		inner.AddAction( () => runs++ );
		outer.Execute();
		inner.Execute();
		Assert.AreEqual( 2, runs );
	}

	[TestMethod]
	public async Task ConcurrentRecursiveInsertionDoesNotDeadlock()
	{
		using var rendezvous = new Barrier( 2 );
		var first = new CommandList( "first" );
		var second = new CommandList( "second" );
		int firstRuns = 0, secondRuns = 0;
		first.AddAction( () => { if ( Interlocked.Increment( ref firstRuns ) == 1 ) rendezvous.SignalAndWait( TimeSpan.FromSeconds( 2 ) ); } );
		second.AddAction( () => { if ( Interlocked.Increment( ref secondRuns ) == 1 ) rendezvous.SignalAndWait( TimeSpan.FromSeconds( 2 ) ); } );
		first.InsertList( second );
		second.InsertList( first );

		var replay = Task.WhenAll( Task.Run( first.Execute ), Task.Run( second.Execute ) );
		await Assert.ThrowsExceptionAsync<InvalidOperationException>( () => replay.WaitAsync( TimeSpan.FromSeconds( 5 ) ) );
	}

	[TestMethod]
	public void DeferredAttributeIntsRemainExact()
	{
		using var scope = Graphics.Scope.CreateOn( HeadlessRenderBackend.Instance );
		var commands = new CommandList();
		const int value = 0x01000001;
		commands.Attributes.Set( "ExactValue", value );
		commands.Execute();
		Assert.AreEqual( value, Graphics.Attributes.GetInt( "ExactValue" ) );
	}

	[TestMethod]
	public void DeferredSizesDistinguishViewportAndNamedDepthTargets()
	{
		using var scope = Graphics.Scope.CreateOn( HeadlessRenderBackend.Instance );
		Graphics.Viewport = new Rect( 0, 0, 91, 73 );
		var list = new CommandList();
		var depth = list.GetRenderTarget( "$vp", 13, 17, ImageFormat.None, ImageFormat.D24S8 );
		list.Attributes.Set( "DepthSize", depth.Size );
		list.Attributes.Set( "ScaledViewport", list.ViewportSizeScaled( 4 ) );
		list.ReleaseRenderTarget( depth );
		list.Execute();

		var depthSize = Graphics.Attributes.GetVector4( "DepthSize" );
		Assert.AreEqual( 13.0f, depthSize.x );
		Assert.AreEqual( 17.0f, depthSize.y );
		var viewport = Graphics.Attributes.GetVector4( "ScaledViewport" );
		Assert.AreEqual( 23.0f, viewport.x );
		Assert.AreEqual( 19.0f, viewport.y );
	}
}
