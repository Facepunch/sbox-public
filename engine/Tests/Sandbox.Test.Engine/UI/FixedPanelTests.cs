using System.Linq;
using Sandbox.Engine;
using Sandbox.Internal;
using Sandbox.UI;

namespace UITests;

[TestClass]
[DoNotParallelize]
public class FixedPanelTests
{
	[TestCleanup]
	public void Cleanup() => GlobalContext.Current.UISystem.Clear();

	private static RootPanel Root() => new() { PanelBounds = new Rect( 100, 80, 800, 600 ) };
	private sealed class ScaledRoot : RootPanel
	{
		public ScaledRoot() => Scale = 2;
	}
	private static Panel Child( Panel parent, string style )
	{
		var panel = parent.AddChild<Panel>();
		panel.Style.Set( style );
		return panel;
	}

	[TestMethod]
	public void TransparentFixedPanelStillFinishesIntro()
	{
		var root = Root();
		var panel = Child( root, "position: fixed; width: 100px; height: 50px; opacity: 0;" );
		root.Layout();
		Assert.IsFalse( panel.IsVisible );
		Assert.IsFalse( panel.HasIntro );
		Assert.AreEqual( new Rect( 100, 80, 100, 50 ), panel.Box.Rect );
	}

	[TestMethod]
	public void SiblingReorderingInvalidatesCachedOverlayOrder()
	{
		var root = Root();
		var a = Child( root, "position: fixed; width: 100px; height: 50px;" );
		var b = Child( root, "position: fixed; width: 100px; height: 50px;" );
		root.Layout();
		root.Layout();
		var point = a.Box.Rect.Center;
		Assert.AreSame( b, root.FindFixedPanelAt( point ) );
		root.SetChildIndex( a, 1 );
		Assert.AreSame( a, root.FindFixedPanelAt( point ) );
		root.SortChildren( ( Panel x, Panel y ) => x == y ? 0 : x == b ? 1 : y == b ? -1 : 0 );
		Assert.AreSame( b, root.FindFixedPanelAt( point ) );
	}

	[TestMethod]
	public void OrdinarySurfacePickingRetainsChildOrder()
	{
		var root = Root();
		var a = Child( root, "position: absolute; width: 100px; height: 50px; z-index: 10; pointer-events: all;" );
		var b = Child( root, "position: absolute; width: 100px; height: 50px; z-index: 1; pointer-events: all;" );
		root.Layout();
		var point = a.Box.Rect.Center;
		Assert.AreSame( b, UISurface.FindPanelAt( root, point, null ) );
		Assert.AreSame( a, ((IPanel)root).GetPanelAt( point, true, true ) );
		Panel hover = null;
		Assert.IsTrue( PanelInput.CheckHover( root, point, ref hover ) );
		Assert.AreSame( a, hover );
	}

	[TestMethod]
	public void ParserPercentCalcViewportOriginAndResize()
	{
		var root = Root();
		root.Style.Set( "padding: 30px; border-width: 10px;" );
		var host = Child( root, "width: 200px; height: 100px; position: relative; left: 50px; top: 40px;" );
		var panel = Child( host, "position: fixed; left: 25%; top: calc(50% - 10px); width: calc(50% - 20px); height: 20%;" );
		root.Layout();
		Assert.AreEqual( PositionMode.Fixed, panel.ComputedStyle.Position );
		Assert.AreEqual( new Rect( 300, 370, 380, 120 ), panel.Box.Rect );
		Assert.AreSame( host, panel.Parent );
		Assert.AreSame( host.LayoutTree.Node, panel.LayoutTree.Node.Owner );

		root.PanelBounds = new Rect( 200, 150, 1000, 800 );
		root.Layout();
		Assert.AreEqual( new Rect( 450, 540, 480, 160 ), panel.Box.Rect );
		root.PanelBounds = new Rect( 230, 170, 1000, 800 );
		root.Layout();
		Assert.AreEqual( new Rect( 480, 560, 480, 160 ), panel.Box.Rect );

		panel.Style.Position = PositionMode.Relative;
		root.Layout();
		panel.Style.Position = PositionMode.Fixed;
		root.Layout();
		Assert.AreEqual( new Rect( 480, 560, 480, 160 ), panel.Box.Rect );
	}

	[TestMethod]
	public void SurfaceScaleAndNegativeViewportOrigin()
	{
		var root = new ScaledRoot { PanelBounds = new Rect( -500, -400, 1000, 800 ), IsWorldPanel = true };
		var host = Child( root, "width: 100px; height: 100px;" );
		var panel = Child( host, "position: fixed; left: 25%; top: 10px; width: 50%; height: 20px;" );
		root.Layout();
		Assert.AreEqual( new Rect( -250, -380, 500, 40 ), panel.Box.Rect );
		Assert.AreSame( panel, root.FindFixedPanelAt( panel.Box.Rect.Center ) );
	}

	[TestMethod]
	public void FloaterEscapesScrollAndContentsWithoutGrowingScrollbars()
	{
		var root = Root();
		var host = Child( root, "width: 150px; height: 100px; overflow: scroll; flex-direction: column;" );
		Child( host, "height: 300px; width: 100px; flex-shrink: 0;" );
		var contents = Child( host, "display: contents;" );
		var nestedContents = Child( contents, "display: contents;" );
		var panel = Child( nestedContents, "position: fixed; left: 500px; top: 400px; width: 100px; height: 50px;" );
		var child = Child( panel, "width: 20px; height: 10px;" );
		root.Layout();
		var scrollSize = host.ScrollSize;
		Assert.AreEqual( 200f, scrollSize.y );
		Assert.AreEqual( new Vector2( 600, 480 ), panel.Box.Rect.Position );
		Assert.AreEqual( panel.Box.Rect.Position, child.Box.Rect.Position );
		host.ScrollOffset = new Vector2( 0, 80 );
		host.SetNeedsFinalLayout();
		root.PostLayout();
		Assert.AreEqual( new Vector2( 600, 480 ), panel.Box.Rect.Position );
		Assert.AreEqual( panel.Box.Rect.Position, child.Box.Rect.Position );
		Assert.AreEqual( scrollSize, host.ScrollSize );
	}

	[TestMethod]
	public void OverlayLifecycleAndLogicalVisibilityOpacity()
	{
		var root = Root();
		var host = Child( root, "width: 100px; height: 100px; opacity: 0.5;" );
		root.Layout();
		var panel = Child( host, "position: fixed; width: 100px; height: 50px; opacity: 0.5;" );
		var nested = Child( panel, "position: fixed; width: 20px; height: 20px; right: 10px;" );
		root.Layout();
		Assert.AreEqual( 2, root.FixedOverlays.Count );
		Assert.AreEqual( 0.25f, panel.Opacity );
		Assert.AreEqual( 0.25f, nested.Opacity );
		Assert.AreEqual( 870f, nested.Box.Rect.Left );
		host.Style.Display = DisplayMode.None;
		root.Layout();
		Assert.AreEqual( 0, root.FixedOverlays.Count );
		Assert.IsFalse( panel.IsVisible );
		host.Style.Display = DisplayMode.Flex;
		root.Layout();
		Assert.AreEqual( 2, root.FixedOverlays.Count );
		Assert.IsTrue( panel.IsVisible );
		host.Style.Opacity = 0;
		root.Layout();
		Assert.IsFalse( panel.IsVisible );
		Assert.IsNull( root.FindFixedPanelAt( panel.Box.Rect.Center ) );
		host.Style.Opacity = 0.5f;
		root.Layout();
		panel.Style.Position = PositionMode.Relative;
		root.Layout();
		Assert.AreEqual( 1, root.FixedOverlays.Count );
		panel.Style.Position = PositionMode.Fixed;
		root.Layout();

		var other = Root();
		other.PanelBounds = new Rect( -200, -100, 400, 300 );
		host.Parent = other;
		root.Layout();
		other.Layout();
		Assert.AreEqual( 0, root.FixedOverlays.Count );
		Assert.AreEqual( 2, other.FixedOverlays.Count );
		Assert.AreEqual( 170f, nested.Box.Rect.Left );
		Assert.AreSame( host, panel.Parent );
		panel.Delete( true );
		Assert.AreEqual( 0, other.FixedOverlays.Count );
	}

	[TestMethod]
	public void AllInputQueriesUseOverlayZOrderAndLogicalEvents()
	{
		var root = Root();
		var host = Child( root, "width: 100px; height: 100px; overflow: hidden; transform: translateX(100px);" );
		var a = Child( host, "position: fixed; left: 400px; top: 300px; width: 100px; height: 80px; pointer-events: all; z-index: 5;" );
		var b = Child( host, "position: fixed; left: 400px; top: 300px; width: 100px; height: 80px; pointer-events: all; z-index: 1;" );
		Child( root, "position: absolute; width: 800px; height: 600px; z-index: 10000; pointer-events: all;" );
		root.Layout();
		// No GPU needed: an ancestor transform must never be visited by overlay picking.
		host.LocalMatrix = Matrix.CreateTranslation( new Vector3( -100, 0, 0 ) );
		var point = new Vector2( 520, 400 );
		Panel hover = null;
		Assert.IsTrue( PanelInput.CheckHover( root, point, ref hover ) );
		Assert.AreSame( a, hover );
		Assert.AreSame( a, UISurface.FindPanelAt( root, point, null ) );
		Assert.AreSame( a, ((IPanel)root).GetPanelAt( point, true, true ) );
		Assert.AreSame( b, UISurface.FindPanelAt( root, point, p => p == b ) );
		b.Style.ZIndex = 5;
		root.Layout();
		Assert.AreSame( b, root.FixedOverlays.Last() );
		Assert.AreSame( b, root.FindFixedPanelAt( point ) );
		b.Style.PointerEvents = PointerEvents.None;
		root.Layout();
		Assert.AreSame( a, root.FindFixedPanelAt( point, needPointerEvents: true ) );
		Assert.AreSame( b, root.FindFixedPanelAt( point ) );
		Assert.IsNull( root.FindFixedPanelAt( new Vector2( 99, 400 ) ) );
		int events = 0;
		host.AddEventListener( "onclick", () => events++ );
		a.DispatchEventImmediate( new MousePanelEvent( "onclick", a, "mouseleft" ) );
		Assert.AreEqual( 1, events );
		host.AcceptsFocus = true;
		Assert.IsTrue( a.Focus() );
		Assert.AreSame( host, GlobalContext.Current.UISystem.NextFocus );
	}

	[TestMethod]
	public void VisualBoundaryRetainsOwnDescendantsButNotAncestorLayersOrMasks()
	{
		var root = Root();
		var host = Child( root, "width: 100px; height: 100px; overflow: hidden; isolation: isolate; transform: rotate(20deg); background-clip: text;" );
		var panel = Child( host, "position: fixed; width: 100px; height: 50px;" );
		var child = Child( panel, "width: 20px; height: 10px;" );
		var nested = Child( child, "position: fixed; width: 10px; height: 10px;" );
		root.Layout();
		Assert.IsTrue( host.HasPanelLayer );
		Assert.IsNull( panel.VisualParent );
		Assert.AreSame( panel, child.VisualParent );
		Assert.AreSame( panel, child.VisualRoot );
		Assert.AreSame( nested, nested.VisualRoot );
		Assert.AreNotSame( host.VisualRoot, panel.VisualRoot );
		Assert.IsFalse( panel.HasPanelLayer );
		Assert.IsTrue( panel.IsOutOfFlow );

		var renderer = GlobalContext.Current.UISystem.Renderer;
		renderer.BuildTransformState( host );
		Assert.IsNotNull( host.GlobalMatrix );
		renderer.ResetFixedOverlayState( root.PanelBounds );
		renderer.BuildTransformState( panel );
		Assert.IsNull( panel.GlobalMatrix );
		Assert.IsNull( panel.LocalMatrix );
		Assert.AreEqual( Matrix.Identity, panel.CachedDescriptors.TransformMat );
		Assert.AreEqual( 1, renderer.ScissorGPU.Count );
		Assert.AreEqual( root.PanelBounds, renderer.ScissorGPU.Clips[0].Rect );
		Assert.AreEqual( Matrix.Identity, renderer.ScissorGPU.Clips[0].Matrix );

		panel.Style.Set( "transform: translateX(20px); overflow: hidden;" );
		root.Layout();
		renderer.BuildTransformState( panel );
		renderer.BuildTransformState( child );
		Assert.IsNotNull( panel.GlobalMatrix );
		Assert.AreEqual( panel.GlobalMatrix, child.GlobalMatrix );
		using ( renderer.Clip( panel ) )
		{
			Assert.AreNotEqual( root.PanelBounds, renderer.Scissor );
		}
		renderer.ResetFixedOverlayState( root.PanelBounds );
		renderer.BuildTransformState( nested );
		Assert.IsNull( nested.GlobalMatrix );
		Assert.AreEqual( root.PanelBounds, renderer.Scissor );
	}
}
