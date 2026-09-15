using Sandbox.Rendering;
using Sandbox.UI;
using System;
using System.Collections.Generic;
using System.Reflection;

namespace EngineTests;

[TestClass]
public class PanelDirectDrawingTest
{
	static readonly Rect Bounds = new( 0, 0, 64, 64 );

	[TestInitialize]
	public void RequireVulkan()
	{
		if ( g_pRenderDevice.GetRenderDeviceAPI() != NativeEngine.RenderDeviceAPI_t.RENDER_DEVICE_API_VULKAN )
			Assert.Inconclusive( "Requires the Vulkan renderer." );
	}

	sealed class ColorPanel : Panel
	{
		internal Color Color = Color.Red;
		internal int Draws;
		internal bool DrewOnMainThread;

		public override void OnDraw( Painter painter )
		{
			Draws++;
			DrewOnMainThread = ThreadSafe.IsMainThread;
			painter.Fill = Color;
			painter.Rect( painter.Bounds );
		}
	}

	sealed class LayerPanel : Panel
	{
		public override void OnDraw( Painter painter ) => DrawLayers( painter );
	}

	sealed class PathPanel : Panel
	{
		public override void OnDraw( Painter painter )
		{
			painter.Stroke = Stroke.Solid( Color.Red, 8 );
			painter.Line( [new Vector2( 8, 12 ), new Vector2( 32, 12 ), new Vector2( 56, 20 )] );
			painter.Stroke = Stroke.None;
			painter.Fill = Color.Red;
			Span<Vector2> points = stackalloc Vector2[12];
			for ( int i = 0; i < points.Length; i++ )
			{
				float angle = i * MathF.Tau / points.Length;
				points[i] = new Vector2( 32, 44 ) + new Vector2( MathF.Cos( angle ), MathF.Sin( angle ) ) * 12;
			}
			painter.Polygon( points );
		}
	}

	[TestMethod]
	public void ManualPathOpacityMatchesPreparedOpacity()
	{
		var system = new UISystem();
		var root = new RootPanel( system ) { PanelBounds = Bounds, RenderedManually = true };
		root.AddChild<PathPanel>().Style.Set( "width: 64px; height: 64px;" );
		using var target = Texture.CreateRenderTarget().WithSize( 64, 64 ).Create();
		try
		{
			root.Layout();
			foreach ( var opacity in new[] { 1f, 0f, 0.5f } )
			{
				root.BuildCommandList( opacity );
				DrawToTarget( target, () => root.Render() );
				using var expected = target.GetBitmap();
				root.BuildCommandList();
				DrawToTarget( target, () => root.RenderManual( opacity ) );
				using var actual = target.GetBitmap();
				CollectionAssert.AreEqual( expected.GetBuffer().ToArray(), actual.GetBuffer().ToArray(), $"Playback opacity {opacity}" );
				if ( opacity == 0 )
				{
					Assert.AreEqual( Color.Transparent, actual.GetPixel( 20, 12 ) );
					Assert.AreEqual( Color.Transparent, actual.GetPixel( 32, 44 ) );
				}
			}
		}
		finally { system.Clear(); }
	}

	sealed class CustomPanel : Panel, IPanelDraw
	{
		public override void OnDraw( Painter painter )
		{
			painter.Fill = Color.Red;
			painter.Rect( painter.Bounds );
		}

		void IPanelDraw.Draw( CommandList commands )
		{
			using var painter = Painter.Begin( commands, Box.Rect );
			painter.Fill = Color.Green;
			painter.Rect( Box.Rect );
		}
	}

	sealed class HorizonClipPanel : Panel
	{
		public override void OnDraw( Painter painter )
		{
			painter.Clip( new Rect( 0, 0, 10, 20 ) );
			painter.Fill = Color.Red;
			painter.Rect( new Rect( -40000, 0, 40010, 40000 ) );
		}
	}

	[TestMethod]
	public void PerspectiveClipDoesNotLeakAtItsInverseHorizon()
	{
		var system = new UISystem();
		var root = new RootPanel( system ) { PanelBounds = Bounds };
		var panel = root.AddChild<HorizonClipPanel>();
		// x maps to x / (1 - x / 20) + 40.4. The inverse horizon is at x = 20.4,
		// well outside the clip, whose projected horizontal extent is [40.4, 60.4].
		panel.Style.Set( "width: 64px; height: 64px; transform-origin: 0% 0%; transform: matrix3d(-1.02,0,0,-0.05,0,1,0,0,0,0,1,0,40.4,0,0,1);" );
		try
		{
			root.Layout();
			using var bitmap = Render( root );
			Assert.AreEqual( Color.Red, bitmap.GetPixel( 48, 10 ), "The projected clip contains the sample." );
			Assert.AreEqual( Color.Transparent, bitmap.GetPixel( 20, 10 ), "The inverse horizon must not introduce a translucent strip." );
		}
		finally { system.Clear(); }
	}

	[TestMethod]
	public void ChildShadowsFollowStyleAndParentChanges()
	{
		var root = new RootPanel { PanelBounds = Bounds };
		var first = root.AddChild<Panel>();
		var second = root.AddChild<Panel>();
		first.Style.Set( "width: 32px; height: 32px;" );
		second.Style.Set( "width: 32px; height: 32px;" );
		try
		{
			Check( 0 );
			first.Style.Set( "box-shadow: 2px 0px 0px red;" );
			Check( 1 );
			second.Style.Set( "box-shadow: 2px 0px 0px blue; z-index: -2;" );
			Check( 2 );
			Assert.AreEqual( Color.Blue, PanelDrawSnapshot.Build( root ).Instances[0].Color );
			first.Style.Set( "box-shadow: none;" );
			Check( 1 );
			second.Parent = first;
			Check( 1 );
			second.Delete( true );
			Check( 0 );
		}
		finally
		{
			root.Delete( true );
		}

		void Check( int count )
		{
			root.Layout();
			Assert.AreEqual( count, PanelDrawSnapshot.Build( root ).Instances.Length );
			Assert.AreEqual( count, PanelDrawSnapshot.Build( root ).Instances.Length );
		}
	}

	[TestMethod]
	public void ChildShadowsAreBehindSiblingContent()
	{
		var system = new UISystem();
		var root = new RootPanel( system ) { PanelBounds = Bounds };
		root.AddChild<Panel>().Style.Set( "position: absolute; left: 8px; top: 16px; width: 32px; height: 32px; background-color: red;" );
		root.AddChild<Panel>().Style.Set( "position: absolute; left: 32px; top: 16px; width: 16px; height: 32px; background-color: blue; box-shadow: -16px 0px 0px black;" );
		try
		{
			root.Layout();
			using var bitmap = Render( root );
			Assert.AreEqual( Color.Red, bitmap.GetPixel( 24, 32 ) );
			Assert.AreEqual( Color.Blue, bitmap.GetPixel( 40, 32 ) );
		}
		finally
		{
			system.Clear();
		}
	}

	sealed class LegacyCustomPanel : Panel, IPanelDraw
	{
		public override void OnDraw( Painter painter )
		{
			painter.Fill = Color.Red;
			painter.Rect( painter.Bounds );
		}

		void IPanelDraw.Draw( CommandList commands )
		{
#pragma warning disable CS0618 // Exercise the legacy command-list drawing path.
			var hud = commands.Paint;
#pragma warning restore CS0618
			hud.SetMatrix( Matrix.CreateTranslation( new Vector3( 24, 0, 0 ) ) );
			hud.SetBlendMode( BlendMode.Multiply );
			hud.DrawRect( new Rect( 0, 0, 16, 32 ), Color.Cyan );
		}
	}

	[TestMethod]
	public void CustomHudDrawingKeepsItsMatrixAndBlendMode()
	{
		var system = new UISystem();
		var root = new RootPanel( system ) { PanelBounds = Bounds };
		root.AddChild<LegacyCustomPanel>().Style.Set( "width: 64px; height: 64px;" );
		try
		{
			root.Layout();
			using var bitmap = Render( root );
			Assert.AreEqual( Color.Red, bitmap.GetPixel( 8, 16 ) );
			Assert.AreEqual( Color.Black, bitmap.GetPixel( 32, 16 ) );
		}
		finally
		{
			system.Clear();
		}
	}

	[TestMethod]
	public void DrawingUpdatesWithoutInvalidationAndReplaysWithoutCallbacks()
	{
		var system = new UISystem();
		var root = new RootPanel( system ) { PanelBounds = Bounds };
		var panel = root.AddChild<ColorPanel>();
		panel.Style.Set( "width: 64px; height: 64px;" );
		try
		{
			root.Layout();
			using var first = Render( root );
			Assert.AreEqual( Color.Red, first.GetPixel( 32, 32 ) );
			panel.Color = Color.Blue;
			using var replay = Render( root, build: false );
			Assert.AreEqual( Color.Red, replay.GetPixel( 32, 32 ) );
			Assert.AreEqual( 1, panel.Draws );
			using var next = Render( root );
			Assert.AreEqual( Color.Blue, next.GetPixel( 32, 32 ) );
			Assert.AreEqual( 2, panel.Draws );
		}
		finally { system.Clear(); }
	}

	[TestMethod]
	public void CustomDrawFollowsContentAndPrecedesOutline()
	{
		var system = new UISystem();
		var root = new RootPanel( system ) { PanelBounds = Bounds };
		var panel = root.AddChild<CustomPanel>();
		panel.Style.Set( "position: absolute; left: 16px; top: 16px; width: 32px; height: 32px; outline: 4px blue;" );
		try
		{
			root.Layout();
			using var bitmap = Render( root );
			Assert.AreEqual( Color.Green, bitmap.GetPixel( 32, 32 ) );
			Assert.AreEqual( Color.Blue, bitmap.GetPixel( 14, 32 ) );
		}
		finally { system.Clear(); }
	}

	[TestMethod]
	public void PanelRecordingRequiresTheMainThread()
	{
		var system = new UISystem();
		var root = new RootPanel( system ) { PanelBounds = Bounds };
		root.AddChild<Panel>().Style.Set( "position: absolute; left: 16px; top: 16px; width: 32px; height: 32px; background-color: red; outline: 4px blue;" );
		try
		{
			root.Layout();
			Task.Run( () =>
			{
				Assert.IsFalse( ThreadSafe.IsMainThread );
				var exception = Assert.ThrowsException<Exception>( () => root.BuildCommandList() );
				StringAssert.Contains( exception.Message, "main thread" );
			} ).GetAwaiter().GetResult();

			using var bitmap = Render( root );
			Assert.AreEqual( Color.Red, bitmap.GetPixel( 32, 32 ) );
			Assert.AreEqual( Color.Blue, bitmap.GetPixel( 14, 32 ) );
			Assert.AreEqual( Color.Transparent, bitmap.GetPixel( 8, 32 ) );
		}
		finally
		{
			system.Clear();
		}
	}

	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void UiBuildPreparesPanelsForRenderThreadReplay( bool manual )
	{
		var system = new UISystem { Size = Bounds.Size };
		var root = new RootPanel( system ) { RenderedManually = manual };
		var panel = root.AddChild<ColorPanel>();
		panel.Style.Set( "width: 100%; height: 100%;" );
		using var target = Texture.CreateRenderTarget().WithSize( 64, 64 ).Create();
		try
		{
			system.LayoutAndBuild();
			Assert.AreEqual( 1, panel.Draws );
			Assert.IsTrue( panel.DrewOnMainThread );
			panel.Color = Color.Blue;

			Task.Run( () =>
			{
				Assert.IsFalse( ThreadSafe.IsMainThread );
				DrawToTarget( target, () => system.Render() );
			} ).GetAwaiter().GetResult();
			using ( var global = target.GetBitmap() )
			{
				Assert.AreEqual( manual ? Color.Transparent : Color.Red, global.GetPixel( 32, 32 ) );
			}

			if ( manual )
			{
				Task.Run( () => DrawToTarget( target, () => root.RenderManual() ) ).GetAwaiter().GetResult();
				using var replay = target.GetBitmap();
				Assert.AreEqual( Color.Red, replay.GetPixel( 32, 32 ) );
			}

			Assert.AreEqual( 1, panel.Draws, "Executing prepared commands must not call OnDraw." );
			system.LayoutAndBuild();
			Assert.AreEqual( 2, panel.Draws );
			Task.Run( () => DrawToTarget( target, () => root.Render() ) ).GetAwaiter().GetResult();
			using var updated = target.GetBitmap();
			Assert.AreEqual( Color.Blue, updated.GetPixel( 32, 32 ) );
		}
		finally
		{
			system.Clear();
		}
	}

	[TestMethod]
	[DataRow( "background-color: red; border: 5px solid blue; outline: 2px cyan; box-shadow: 2px 2px 3px green;" )]
	[DataRow( "background: linear-gradient( red, blue ); border: 5px solid green;" )]
	[DataRow( "background-color: red; filter: blur( 2px );" )]
	[DataRow( "background-color: red; backdrop-filter: invert( 1 );" )]
	public void ManualOpacityMatchesPreparedOpacity( string style )
	{
		var system = new UISystem();
		var root = new RootPanel( system ) { PanelBounds = Bounds, RenderedManually = true };
		var panel = root.AddChild<Panel>();
		panel.Style.Set( "position: absolute; left: 8px; top: 8px; width: 48px; height: 48px; border-radius: 8px; " + style );
		var label = panel.AddChild<Label>();
		label.Text = "Text";
		label.Style.Set( "font-size: 12px; color: white;" );
		using var target = Texture.CreateRenderTarget().WithSize( 64, 64 ).Create();
		try
		{
			root.Layout();
			foreach ( var opacity in new[] { 0f, 0.25f, 0.5f, 1f } )
			{
				root.BuildCommandList( opacity );
				DrawToTarget( target, () => root.Render() );
				using var expected = target.GetBitmap();
				root.BuildCommandList();
				Task.Run( () => DrawToTarget( target, () => root.RenderManual( opacity ) ) ).GetAwaiter().GetResult();
				using var actual = target.GetBitmap();
				var expectedBytes = expected.GetBuffer();
				var actualBytes = actual.GetBuffer();
				int maxDifference = 0;
				for ( int i = 0; i < expectedBytes.Length; i++ )
				{
					maxDifference = Math.Max( maxDifference, Math.Abs( expectedBytes[i] - actualBytes[i] ) );
				}

				// Backdrop quad colors are packed to eight bits; replay opacity retains float precision.
				Assert.IsTrue( maxDifference <= 1, $"Opacity {opacity}: maximum channel difference {maxDifference}." );
			}
		}
		finally
		{
			system.Clear();
		}
	}

	[TestMethod]
	public void NestedCssLayersRestoreThePlaybackTargetAndViewport()
	{
		var system = new UISystem();
		var root = new RootPanel( system ) { PanelBounds = Bounds, RenderedManually = true };
		var outer = root.AddChild<Panel>();
		outer.Style.Set( "width: 64px; height: 64px; isolation: isolate;" );
		var inner = outer.AddChild<ColorPanel>();
		inner.Style.Set( "width: 64px; height: 64px; isolation: isolate;" );
		try
		{
			root.Layout();
			root.BuildCommandList();
			foreach ( var size in new[] { 64, 96 } )
			{
				using var target = Texture.CreateRenderTarget().WithSize( size, size ).Create();
				Task.Run( () => DrawToTarget( target, () =>
				{
					var destination = Graphics.RenderTarget;
					var viewport = new NativeEngine.RenderViewport( new Rect( 4, 4, size - 8, size - 8 ), 0.2f, 0.8f );
					Graphics.Context.SetViewport( viewport );
					root.RenderManual();
					Assert.AreSame( destination, Graphics.RenderTarget );
					var restored = Graphics.Context.GetViewport();
					Assert.AreEqual( viewport.Rect, restored.Rect );
					Assert.AreEqual( viewport.MinZ, restored.MinZ );
					Assert.AreEqual( viewport.MaxZ, restored.MaxZ );
				} ) ).GetAwaiter().GetResult();
				using var bitmap = target.GetBitmap();
				Assert.AreEqual( Color.Red, bitmap.GetPixel( 32, 32 ) );
				Assert.AreEqual( Color.Transparent, bitmap.GetPixel( 2, 2 ) );
				Assert.AreEqual( 1, inner.Draws );
			}
		}
		finally
		{
			system.Clear();
		}
	}

	[TestMethod]
	public void ManualOpacityIsRestoredBetweenExecutions()
	{
		var system = new UISystem();
		var root = new RootPanel( system ) { PanelBounds = Bounds, RenderedManually = true };
		var panel = root.AddChild<ColorPanel>();
		panel.Style.Set( "width: 64px; height: 64px;" );
		using var target = Texture.CreateRenderTarget().WithSize( 64, 64 ).Create();
		try
		{
			root.Layout();
			root.BuildCommandList();
			Task.Run( () => DrawToTarget( target, () =>
			{
				Graphics.Attributes.Set( "UIPanelOpacity", 0.75f );
				Graphics.Attributes.SetCombo( "D_PANEL_OPACITY", true );
				root.RenderManual( 0.25f );
				Assert.AreEqual( 0.75f, Graphics.Attributes.GetFloat( "UIPanelOpacity" ) );
				Assert.IsTrue( Graphics.Attributes.GetComboBool( "D_PANEL_OPACITY" ) );
				Graphics.Clear( Color.Transparent, clearDepth: false, clearStencil: false );
				root.RenderManual();
			} ) ).GetAwaiter().GetResult();
			using var bitmap = target.GetBitmap();
			Assert.AreEqual( Color.Red, bitmap.GetPixel( 32, 32 ) );
			Assert.AreEqual( 1, panel.Draws );
		}
		finally
		{
			system.Clear();
		}
	}

	[TestMethod]
	public void AppearanceChangesMatchFreshPanels()
	{
		using var pixels = new Bitmap( 12, 8 );
		for ( int y = 0; y < 8; y++ )
		{
			for ( int x = 0; x < 12; x++ ) pixels.SetPixel( x, y, x < 6 ? Color.Cyan : Color.Blue );
		}
		using var texture = pixels.ToTexture( false );
		var system = new UISystem();
		var root = new RootPanel( system ) { PanelBounds = Bounds };

		static void Apply( RootPanel root, Texture texture, int variant )
		{
			var panel = root.Children.FirstOrDefault() ?? root.AddChild<Panel>();
			panel.Style.Set( "position: absolute; left: 3px; top: 3px; width: 56px; height: 56px; border: 2px solid red; border-radius: 15%; padding: 3px; background-color: white; background-size: 12px 8px; background-position: 0px 0px; box-shadow: 1px 1px 2px black, inset 1px 1px 1px black; outline: 1px blue; overflow: hidden;" );
			panel.Style.BackgroundImage = texture;
			if ( variant == 1 ) panel.Style.Set( "width: 44px; height: 48px; border-width: 5px; border-radius: 40%; background-size: 18px 10px; background-position: 5px 3px;" );
			if ( variant == 2 ) panel.Style.Set( "left: 8px; top: 6px; width: 50px; height: 44px; border-width: 1px; border-radius: 2px; background-size: cover; background-position: 50% 50%;" );
			var label = panel.Children.OfType<Label>().FirstOrDefault() ?? panel.AddChild<Label>();
			label.Style.Set( $"position: absolute; left: 2px; top: 20px; width: 34px; height: 20px; font-size: 9px; color: black; text-align: {(variant == 1 ? "center" : variant == 2 ? "right" : "left")}; align-items: {(variant == 2 ? "flex-end" : "center")};" );
			label.Text = variant == 1 ? "Longer" : "Hi";
			var image = panel.Children.OfType<Image>().FirstOrDefault() ?? panel.AddChild<Image>();
			image.Style.Set( $"position: absolute; left: 2px; top: 2px; width: 28px; height: 16px; object-fit: {(variant == 1 ? "contain" : "cover")};" );
			image.Texture = texture;
			root.Layout();
		}

		try
		{
			foreach ( int variant in new[] { 0, 1, 2, 0 } )
			{
				Apply( root, texture, variant );
				using var actual = Render( root );
				var freshSystem = new UISystem();
				try
				{
					var fresh = new RootPanel( freshSystem ) { PanelBounds = Bounds };
					Apply( fresh, texture, variant );
					using var expected = Render( fresh );
					CollectionAssert.AreEqual( expected.GetBuffer().ToArray(), actual.GetBuffer().ToArray(), $"Appearance variant {variant}" );
				}
				finally { freshSystem.Clear(); }
			}
		}
		finally { system.Clear(); }
	}

	static void DrawLayers( Painter painter )
	{
		painter.Clip( Bounds.Shrink( 4 ), 6 );
		using var outer = painter.BeginLayer( Bounds, 0.8f, new Painter.Filter { Blur = 1 } );
		painter.Fill = Color.Red;
		painter.Rect( Bounds.Shrink( 8 ), 8 );
		using var inner = painter.BeginLayer( Bounds.Shrink( 16 ), 0.5f, mask: new Painter.Mask( Texture.White, Bounds.Shrink( 16 ) ) );
		painter.Fill = Color.Blue;
		painter.Circle( Bounds.Center, 12 );
	}

	[TestMethod]
	public void NestedLayersMatchTexturePaintingAndReleaseTheirTargets()
	{
		var system = new UISystem();
		var root = new RootPanel( system ) { PanelBounds = Bounds };
		var panel = root.AddChild<LayerPanel>();
		panel.Style.Set( "width: 64px; height: 64px;" );
		using var reference = Texture.CreateRenderTarget().WithSize( 64, 64 ).Create();
		using ( var painter = Painter.Begin( reference ) )
		{
			painter.Clear( Color.Transparent );
			DrawLayers( painter );
		}
		using var expected = reference.GetBitmap();
		var targets = (List<RenderTarget>)typeof( RenderTarget ).GetField( "All", BindingFlags.Static | BindingFlags.NonPublic ).GetValue( null );
		var borrowed = targets.Where( target => target.Loaned ).ToArray();
		try
		{
			root.Layout();
			for ( int i = 0; i < 8; i++ )
			{
				using var actual = Render( root );
				CollectionAssert.AreEqual( expected.GetBuffer().ToArray(), actual.GetBuffer().ToArray() );
				CollectionAssert.AreEquivalent( borrowed, targets.Where( target => target.Loaned ).ToArray(), "Layer targets return to the pool after execution." );
			}
			root.Delete();
		}
		finally { system.Clear(); }
	}

	static T Field<T>( object value, string name )
	{
		return (T)value.GetType().GetField( name, BindingFlags.Instance | BindingFlags.NonPublic ).GetValue( value );
	}

	static Bitmap Render( RootPanel root, bool build = true )
	{
		if ( build )
		{
			root.BuildCommandList();
		}
		using var target = Texture.CreateRenderTarget().WithSize( 64, 64 ).Create();
		target.Flags |= TextureFlags.PremultipliedAlpha;
		DrawToTarget( target, () => root.Render() );
		return target.GetBitmap();
	}

	static void DrawToTarget( Texture target, Action draw )
	{
		using var scope = Graphics.Scope.Create();
		scope.Attributes.Set( "UIGammaOutput", true );
		scope.Attributes.SetCombo( "D_NO_ZTEST", 1 );
		Graphics.RenderTarget = RenderTarget.From( target );
		scope.Context.Clear( Color.Transparent, true, false, false );
		draw();
	}
}
