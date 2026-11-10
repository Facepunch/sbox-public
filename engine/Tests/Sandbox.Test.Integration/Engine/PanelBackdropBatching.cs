using Sandbox.Rendering;
using Sandbox.UI;
using System;

namespace EngineTests;

[TestClass]
public class PanelBackdropBatchingTest
{
	const int Width = 769;
	const int Height = 385;

	[TestInitialize]
	public void RequireVulkan()
	{
		if ( g_pRenderDevice.GetRenderDeviceAPI() != NativeEngine.RenderDeviceAPI_t.RENDER_DEVICE_API_VULKAN )
			Assert.Inconclusive( "Requires the Vulkan renderer." );
	}

	sealed class Pattern : Panel
	{
		public override void OnDraw( Painter painter )
		{
			for ( int y = 0; y < Height; y += 17 )
				for ( int x = 0; x < Width; x += 19 )
				{
					painter.Fill = new Color( (x % 113) / 113f, (y % 97) / 97f, ((x + y) % 83) / 83f );
					painter.Rect( new Rect( x, y, 19, 17 ) );
				}
		}
	}

	sealed class NativeBarrier : Panel, IPanelDraw
	{
		void IPanelDraw.Draw( CommandList commands )
		{
			using var painter = Painter.Begin( commands, new Rect( 0, 0, Width, Height ) );
			painter.Fill = Color.Magenta;
			painter.Rect( new Rect( 540, 44, 60, 100 ) );
		}
	}

	[TestMethod]
	[DataRow( 0f, false )]
	[DataRow( 2f, false )]
	[DataRow( 8f, false )]
	[DataRow( 10f, false )]
	[DataRow( 24f, false )]
	[DataRow( 8f, true )]
	public void IndependentBackdropsShareAndBatchExactly( float blur, bool transform )
	{
		Compare( blur, 540, transform, false, true );
	}

	[TestMethod]
	[DataRow( 0f, 110, false )]
	[DataRow( 8f, 153, false )]
	[DataRow( 10f, 175, false )]
	[DataRow( 8f, 540, true )]
	public void DependentBackdropsAndNativeCommandsAreBarriers( float blur, int left, bool native )
	{
		Compare( blur, left, false, native, false );
	}

	[TestMethod]
	[DataRow( "world" )]
	[DataRow( "layer" )]
	[DataRow( "perspective" )]
	[DataRow( "viewport" )]
	public void UnsupportedDestinationsKeepSeparateGrabs( string kind )
	{
		var share = PainterBatcher.ShareBackdrops;
		PainterBatcher.ShareBackdrops = true;
		var commands = new CommandList();
		try
		{
			using var painter = Painter.Begin( commands, new Rect( 0, 0, Width, Height ) );
			var bounds = new Rect( kind == "viewport" ? 5 : 0, 0, Width, Height );
			painter.SetViewport( bounds, kind == "world" ? Matrix.Identity : null );
			var matrix = Matrix.Identity;
			if ( kind == "perspective" ) matrix.M14 = 0.001f;
			using var destination = painter.WithDestination( bounds, 1, 1, BlendMode.Normal, matrix );
			using var layer = kind == "layer" ? painter.Target( "test-layer", bounds ) : default;
			painter.FilterBackdrop( new Rect( 48, 48, 100, 180 ), new Painter.Filter { Blur = 8 }, BorderRadii.Zero );
			painter.FilterBackdrop( new Rect( 540, 48, 100, 180 ), new Painter.Filter { Blur = 8 }, BorderRadii.Zero );
			Assert.AreEqual( 2, painter.FrameGrabCount );
		}
		finally
		{
			commands.Reset();
			PainterBatcher.ShareBackdrops = share;
		}
	}

	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void SiblingPolicyPreservesOverlapAndNativeBarriers( bool native )
	{
		Compare( 8, native ? 540 : 110, false, native, false, sampleGuard: false );
	}

	[TestMethod]
	public void NestedBackdropSeesItsParentDrawing()
	{
		Compare( 8, 540, false, false, false, sampleGuard: false, nested: true );
	}

	[TestMethod]
	public void ViewportChangeFlushesPendingDrawingAndDiscardsCapture()
	{
		var share = PainterBatcher.ShareBackdrops;
		PainterBatcher.ShareBackdrops = true;
		var commands = new CommandList();
		try
		{
			using var painter = Painter.Begin( commands, new Rect( 0, 0, Width, Height ) );
			painter.SetViewport( painter.Bounds );
			painter.FilterBackdrop( new Rect( 48, 48, 100, 180 ), new Painter.Filter { Blur = 8 }, BorderRadii.Zero );
			painter.Fill = Color.White;
			painter.Rect( new Rect( 48, 48, 20, 20 ) );
			Assert.AreEqual( 1, painter.DrawCount );

			painter.SetViewport( new Rect( 0, 0, Width + 32, Height ) );
			Assert.AreEqual( 2, painter.DrawCount, "Pending instances belong to the old viewport." );
			painter.FilterBackdrop( new Rect( 540, 48, 100, 180 ), new Painter.Filter { Blur = 8 }, BorderRadii.Zero );
			Assert.AreEqual( 2, painter.FrameGrabCount, "A new viewport needs a fresh capture." );
		}
		finally
		{
			commands.Reset();
			PainterBatcher.ShareBackdrops = share;
		}
	}

	[TestMethod]
	public void RewindDiscardsTheSharedCapture()
	{
		var commands = new CommandList();
		try
		{
			using var painter = Painter.Begin( commands, new Rect( 0, 0, Width, Height ) );
			painter.SetViewport( painter.Bounds );
			var batcher = painter.ActiveContext.Batcher;
			var batch = batcher.GetCheckpoint();
			var checkpoint = commands.GetCheckpoint();
			painter.FilterBackdrop( new Rect( 48, 48, 100, 180 ), new Painter.Filter { Blur = 8 }, BorderRadii.Zero );
			batcher.Rewind( batch );
			commands.Rewind( checkpoint );
			painter.FilterBackdrop( new Rect( 540, 48, 100, 180 ), new Painter.Filter { Blur = 8 }, BorderRadii.Zero );
			Assert.AreEqual( 1, painter.FrameGrabCount, "The replacement recording needs its own capture." );
		}
		finally { commands.Reset(); }
	}

	static void Compare( float blur, int left, bool transform, bool native, bool expectSharing, bool sampleGuard = true, bool nested = false )
	{
		var share = PainterBatcher.ShareBackdrops;
		var defer = PainterBatcher.DeferBatches;
		var guard = PainterBatcher.BackdropSampleGuard;
		var system = new UISystem();
		var root = new RootPanel( system ) { PanelBounds = new Rect( 0, 0, Width, Height ) };
		root.AddChild<Pattern>().Style.Set( $"position: absolute; width: {Width}px; height: {Height}px;" );
		var first = AddCard( root, 48, blur );
		if ( native ) root.AddChild<NativeBarrier>().Style.Set( "position: absolute; width: 1px; height: 1px;" );
		var second = AddCard( root, left, blur );
		if ( nested )
		{
			second.Parent = first;
			second.Style.Set( "left: 20px; top: 20px; width: 50px; height: 60px;" );
		}
		if ( transform )
		{
			first.Style.Set( "transform: rotate(7deg) scale(0.9); overflow: hidden;" );
			second.Style.Set( "transform: rotate(-9deg) scale(1.1); overflow: hidden;" );
		}
		try
		{
			PainterBatcher.BackdropSampleGuard = sampleGuard;
			root.Layout();
			PainterBatcher.ShareBackdrops = false;
			PainterBatcher.DeferBatches = false;
			using var expected = Render( root );
			var original = root.Stats;
			Assert.AreEqual( 2, original.FrameGrabs );
			foreach ( var deferred in new[] { false, true } )
			{
				PainterBatcher.ShareBackdrops = true;
				PainterBatcher.DeferBatches = deferred;
				using var actual = Render( root );
				CollectionAssert.AreEqual( expected.GetBuffer().ToArray(), actual.GetBuffer().ToArray(), $"deferred={deferred}" );
				Assert.AreEqual( expectSharing ? 1 : 2, root.Stats.FrameGrabs );
				Assert.AreEqual( original.Instances, root.Stats.Instances );
				if ( expectSharing && deferred ) Assert.IsTrue( root.Stats.DrawCalls < original.DrawCalls );
				using var replay = Render( root, false );
				CollectionAssert.AreEqual( expected.GetBuffer().ToArray(), replay.GetBuffer().ToArray(), "Command-list replay" );
			}
		}
		finally
		{
			PainterBatcher.ShareBackdrops = share;
			PainterBatcher.DeferBatches = defer;
			PainterBatcher.BackdropSampleGuard = guard;
			system.Clear();
		}
	}

	static Panel AddCard( Panel root, int left, float blur )
	{
		var card = root.AddChild<Panel>();
		card.Style.Set( FormattableString.Invariant( $"position: absolute; left: {left}px; top: 48px; width: 100px; height: 180px; border-radius: 13px; background-color: rgba(20, 30, 70, 0.4); backdrop-filter: blur({blur}px) brightness(0.8); border: 2px solid rgba(255,255,255,0.5);" ) );
		card.AddChild<Panel>().Style.Set( "margin: 14px; width: 65px; height: 100px; background-color: rgba(255,50,20,0.7);" );
		return card;
	}

	[TestMethod]
	[DataRow( 8f, 16 )]
	[DataRow( 10f, 16 )]
	[DataRow( 24f, 16 )]
	public void MeasureSiblingBlurEdgeDifference( float blur, int gap )
	{
		var share = PainterBatcher.ShareBackdrops;
		var defer = PainterBatcher.DeferBatches;
		var guard = PainterBatcher.BackdropSampleGuard;
		var system = new UISystem();
		var root = new RootPanel( system ) { PanelBounds = new Rect( 0, 0, Width, Height ) };
		root.AddChild<Pattern>().Style.Set( $"position: absolute; width: {Width}px; height: {Height}px;" );
		AddCard( root, 48, blur );
		AddCard( root, 148 + gap, blur );
		try
		{
			root.Layout();
			PainterBatcher.ShareBackdrops = false;
			using var expected = Render( root );
			PainterBatcher.ShareBackdrops = true;
			PainterBatcher.DeferBatches = true;
			PainterBatcher.BackdropSampleGuard = false;
			using var actual = Render( root );
			Assert.AreEqual( 1, root.Stats.FrameGrabs );
			var a = expected.GetBuffer();
			var b = actual.GetBuffer();
			int max = 0, changed = 0;
			long sum = 0;
			for ( int i = 0; i < a.Length; i++ )
			{
				int difference = Math.Abs( a[i] - b[i] );
				max = Math.Max( max, difference );
				sum += difference;
				if ( difference != 0 ) changed++;
			}
			Console.WriteLine( $"Sibling blur {blur}, gap {gap}: max channel delta {max}/255, mean {sum / (double)a.Length:F6}/255, changed bytes {changed}/{a.Length}" );
			var capture = Environment.GetEnvironmentVariable( "SBOX_TEST_CAPTURE" );
			if ( capture is not null )
			{
				System.IO.File.WriteAllBytes( System.IO.Path.Combine( capture, $"blur-{blur}-separate.png" ), expected.ToPng() );
				System.IO.File.WriteAllBytes( System.IO.Path.Combine( capture, $"blur-{blur}-shared.png" ), actual.ToPng() );
			}
		}
		finally
		{
			PainterBatcher.ShareBackdrops = share;
			PainterBatcher.DeferBatches = defer;
			PainterBatcher.BackdropSampleGuard = guard;
			system.Clear();
		}
	}

	[TestMethod]
	public void GrayscaleBackdropPreservesLuminance()
	{
		var system = new UISystem();
		var root = new RootPanel( system ) { PanelBounds = new Rect( 0, 0, Width, Height ) };
		try
		{
			root.AddChild<Panel>().Style.Set( "position: absolute; left: 20px; top: 20px; width: 160px; height: 160px; background-color: #29d8ff; filter: grayscale(1);" );
			root.AddChild<Panel>().Style.Set( "position: absolute; left: 220px; top: 20px; width: 160px; height: 160px; background-color: #29d8ff;" );
			root.AddChild<Panel>().Style.Set( "position: absolute; left: 220px; top: 20px; width: 160px; height: 160px; backdrop-filter: grayscale(1);" );
			root.Layout();
			using var bitmap = Render( root );
			var pixels = bitmap.GetBuffer();
			int regular = (100 * Width + 100) * 4;
			int backdrop = (100 * Width + 300) * 4;
			for ( int channel = 0; channel < 3; channel++ )
			{
				Assert.IsTrue( Math.Abs( pixels[regular + channel] - pixels[backdrop + channel] ) <= 2, "Backdrop grayscale should match the ordinary filter." );
				Assert.IsTrue( pixels[backdrop + channel] is > 170 and < 195, "Cyan's grayscale luminance must remain gray, not turn white." );
			}
		}
		finally { system.Clear(); }
	}

	[TestMethod]
	public void PerspectiveInsideClipPreservesTheWholeFace()
	{
		var system = new UISystem();
		var root = new RootPanel( system ) { PanelBounds = new Rect( 0, 0, Width, Height ) };
		try
		{
			var parent = root.AddChild<Panel>();
			parent.Style.Set( "position: absolute; left: 400px; top: 30px; width: 300px; height: 300px;" );
			parent.AddChild<Panel>().Style.Set( "position: absolute; left: 100px; top: 100px; width: 100px; height: 100px; background-color: #29d8ff; transform: perspective(240px) rotateX(-22deg) rotateY(-32deg) translateZ(50px);" );
			root.Layout();
			using var expected = Render( root );
			parent.Style.Set( "overflow", "hidden" );
			root.Layout();
			using var actual = Render( root );
			var a = expected.GetBuffer();
			var b = actual.GetBuffer();
			for ( int i = 0; i < a.Length; i++ )
				Assert.IsTrue( Math.Abs( a[i] - b[i] ) <= 2, $"Clipping cut into the perspective face at byte {i}." );
		}
		finally { system.Clear(); }
	}
	static Bitmap Render( RootPanel root, bool build = true )
	{
		if ( build ) root.BuildCommandList();
		using var target = Texture.CreateRenderTarget().WithSize( Width, Height ).Create();
		target.Flags |= TextureFlags.PremultipliedAlpha;
		using ( var scope = Graphics.Scope.Create() )
		{
			scope.Attributes.Set( "UIGammaOutput", true );
			scope.Attributes.SetCombo( "D_NO_ZTEST", 1 );
			Graphics.RenderTarget = RenderTarget.From( target );
			scope.Context.Clear( Color.Transparent, true, false, false );
			root.Render();
		}
		return target.GetBitmap();
	}
}
