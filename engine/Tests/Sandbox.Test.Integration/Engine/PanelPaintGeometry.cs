using Sandbox.Rendering;
using Sandbox.UI;
using System.Reflection;

namespace EngineTests;

[TestClass]
public class PanelPaintGeometryTest
{
	[TestInitialize]
	public void RequireVulkan()
	{
		if ( g_pRenderDevice.GetRenderDeviceAPI() != NativeEngine.RenderDeviceAPI_t.RENDER_DEVICE_API_VULKAN )
			Assert.Inconclusive( "Requires the Vulkan renderer." );
	}

	sealed class ScaledRoot : RootPanel
	{
		internal void SetScale( float scale ) => Scale = scale;
	}

	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void LayoutPreparesPaintWithoutChangingItDuringDrawing( bool layered )
	{
		var root = new RootPanel { PanelBounds = new Rect( 0, 0, 400, 300 ) };
		var panel = root.AddChild<Panel>();
		panel.Style.Set( "width: 100px; height: 100px; background: linear-gradient( red, blue ); border-radius: 1em; font-size: 12px; transform: rotate(10deg);" );
		if ( layered )
		{
			panel.Style.Set( "filter: blur(1px);" );
			panel.Style.MaskImage = Texture.White;
		}
		var child = panel.AddChild<Image>();
		child.Texture = Texture.White;
		child.Style.Set( "width: 20px; height: 20px; object-fit: contain;" );
		try
		{
			root.Layout();
			root.Layout();
			Assert.IsTrue( BackgroundHasFill( panel ) );
			Assert.IsNotNull( panel.GlobalMatrix );
			Assert.AreEqual( panel.RenderTransform, child.RenderTransform );
			var cache = Cache( panel );
			var content = Cache( child );
			var matrix = panel.GlobalMatrix;
			var first = PanelDrawSnapshot.Build( root );
			CollectionAssert.AreEqual( first.Instances, PanelDrawSnapshot.Build( root ).Instances );
			Assert.AreEqual( cache, Cache( panel ) );
			Assert.AreEqual( content, Cache( child ) );
			Assert.AreEqual( matrix, panel.GlobalMatrix );
		}
		finally
		{
			root.Delete( true );
		}
	}

	[TestMethod]
	public void TransformOriginUpdatesDescendantsDuringLayout()
	{
		var root = new RootPanel { PanelBounds = new Rect( 0, 0, 400, 300 ) };
		var panel = root.AddChild<Panel>();
		panel.Style.Set( "width: 100px; height: 100px; transform: rotate(30deg); transform-origin: 0% 0%;" );
		var child = panel.AddChild<Panel>();
		child.Style.Set( "width: 20px; height: 20px;" );
		try
		{
			root.Layout();
			root.Layout();
			var first = child.RenderTransform;
			panel.Style.Set( "transform-origin: 100% 100%;" );
			root.Layout();
			Assert.AreNotEqual( first, child.RenderTransform );
			Assert.AreEqual( panel.RenderTransform, child.RenderTransform );
			panel.Style.Set( "isolation: isolate;" );
			root.Layout();
			Assert.IsTrue( panel.HasPanelLayer );
			Assert.AreEqual( panel.RenderTransform, child.RenderTransform );
			panel.Style.Set( "isolation: auto;" );
			root.Layout();
			Assert.AreEqual( panel.RenderTransform, child.RenderTransform );
		}
		finally
		{
			root.Delete( true );
		}
	}

	static object Cache( Panel panel )
	{
		return typeof( Panel ).GetField( "_paintCache", BindingFlags.Instance | BindingFlags.NonPublic ).GetValue( panel );
	}

	[TestMethod]
	public void ContentChangesKeepAncestorPaintGeometry()
	{
		var root = new RootPanel { PanelBounds = new Rect( 0, 0, 400, 300 ) };
		var panel = root.AddChild<Panel>();
		panel.Style.Set( "width: 100px; height: 100px; background: linear-gradient( red, blue ); border-radius: 10px;" );
		var label = panel.AddChild<Label>();
		label.Text = "First";
		try
		{
			root.Layout();
			root.Layout();
			PanelDrawSnapshot.Build( root );
			Assert.IsTrue( BackgroundHasFill( panel ) );

			label.Text = "Second";
			root.PreLayout();
			Assert.IsFalse( PaintGeometryIsDirty( panel ) );
			root.CalculateLayout();
			root.PostLayout();
			Assert.IsTrue( BackgroundHasFill( panel ) );

			panel.SetNeedsPreLayout();
			root.PreLayout();
			Assert.IsFalse( PaintGeometryIsDirty( panel ) );

			root.Style.Opacity = 0.5f;
			root.PreLayout();
			Assert.IsFalse( PaintGeometryIsDirty( panel ), "An inherited opacity change does not change the panel's geometry or fill." );
		}
		finally
		{
			root.Delete( true );
		}
	}

	[TestMethod]
	public void PendingGeometryChangesSurviveUnchangedLayout()
	{
		var root = new RootPanel { PanelBounds = new Rect( 0, 0, 400, 300 ) };
		var panel = root.AddChild<Panel>();
		panel.Style.Set( "width: 100px; height: 100px; background-color: red; border-radius: 10px;" );
		try
		{
			root.Layout();
			root.Layout();
			panel.Style.Set( "border-radius: 20px;" );
			root.PreLayout();
			Assert.IsTrue( PaintGeometryIsDirty( panel ) );

			panel.SetNeedsPreLayout();
			root.PreLayout();
			Assert.IsTrue( PaintGeometryIsDirty( panel ) );
			root.CalculateLayout();
			root.PostLayout();
			Assert.IsFalse( PaintGeometryIsDirty( panel ) );
			Assert.AreEqual( new Vector4( 20 ), PanelDrawSnapshot.Build( root ).Instances.Single().BorderRadius );
		}
		finally
		{
			root.Delete( true );
		}
	}

	static bool PaintGeometryIsDirty( Panel panel )
	{
		var cache = typeof( Panel ).GetField( "_paintCache", BindingFlags.Instance | BindingFlags.NonPublic ).GetValue( panel );
		return (bool)cache.GetType().GetField( "_dirty", BindingFlags.Instance | BindingFlags.NonPublic ).GetValue( cache );
	}

	static bool BackgroundHasFill( Panel panel )
	{
		var cache = typeof( Panel ).GetField( "_paintCache", BindingFlags.Instance | BindingFlags.NonPublic ).GetValue( panel );
		var placement = cache.GetType().GetField( "Background", BindingFlags.Instance | BindingFlags.NonPublic ).GetValue( cache );
		return (bool)placement.GetType().GetField( "HasFill", BindingFlags.Instance | BindingFlags.NonPublic ).GetValue( placement );
	}

	[TestMethod]
	public void ImagePlacementFollowsStyleBoundsAndTextureSize()
	{
		using var image = Texture.CreateRenderTarget().WithSize( 20, 10 ).Create();
		using var replacement = Texture.CreateRenderTarget().WithSize( 40, 40 ).Create();
		var root = new RootPanel { PanelBounds = new Rect( 0, 0, 400, 300 ) };
		var panel = root.AddChild<Panel>();
		panel.Style.Set( "width: 100px; height: 100px; background-size: contain; background-position: 10px 20px;" );
		panel.Style.BackgroundImage = image;
		try
		{
			root.Layout();
			Assert.AreEqual( new Vector4( 10, 20, 100, 50 ), Tile() );
			Assert.AreEqual( new Vector4( 10, 20, 100, 50 ), Tile() );
			panel.Style.Set( "background-size: cover;" );
			root.Layout();
			Assert.AreEqual( new Vector4( 10, 20, 200, 100 ), Tile() );
			image.CopyFrom( replacement );
			root.Layout();
			Assert.AreEqual( new Vector4( 10, 20, 100, 100 ), Tile() );
			panel.Style.Set( "width: 50%;" );
			root.Layout();
			Assert.AreEqual( new Vector4( 10, 20, 200, 200 ), Tile() );
			root.PanelBounds = new Rect( 0, 0, 600, 300 );
			root.Layout();
			Assert.AreEqual( new Vector4( 10, 20, 300, 300 ), Tile() );
		}
		finally
		{
			root.Delete( true );
		}

		Vector4 Tile() => PanelDrawSnapshot.Build( root ).Instances.Single().BackgroundRect;
	}

	[TestMethod]
	public void ImageTextureChangesPreparePlacementImmediately()
	{
		using var wide = Texture.CreateRenderTarget().WithSize( 20, 10 ).Create();
		using var square = Texture.CreateRenderTarget().WithSize( 10, 10 ).Create();
		var root = new RootPanel { PanelBounds = new Rect( 0, 0, 200, 200 ) };
		var image = root.AddChild<Image>();
		image.Style.Set( "width: 100px; height: 100px; object-fit: contain;" );
		image.Texture = wide;
		try
		{
			root.Layout();
			Assert.AreEqual( new Vector4( 0, 0, 100, 50 ), PanelDrawSnapshot.Build( root ).Instances.Single().BackgroundRect );
			image.Texture = square;
			Assert.AreEqual( new Vector4( 0, 0, 100, 100 ), PanelDrawSnapshot.Build( root ).Instances.Single().BackgroundRect );
		}
		finally
		{
			root.Delete( true );
		}
	}

	[TestMethod]
	public void InheritedPaintStylesRefreshTheCache()
	{
		var root = new RootPanel { PanelBounds = new Rect( 0, 0, 200, 200 ) };
		root.Style.Set( "color: red; border-radius: 8px;" );
		var panel = root.AddChild<Panel>();
		panel.Style.Set( "width: 100px; height: 100px; background-color: currentColor; border-radius: inherit;" );
		try
		{
			root.Layout();
			var first = PanelDrawSnapshot.Build( root ).Instances.Single();
			Assert.AreEqual( Color.Red, first.Color );
			Assert.AreEqual( new Vector4( 8 ), first.BorderRadius );
			root.Style.Set( "color: blue; border-radius: 12px;" );
			root.Layout();
			var updated = PanelDrawSnapshot.Build( root ).Instances.Single();
			Assert.AreEqual( Color.Blue, updated.Color );
			Assert.AreEqual( new Vector4( 12 ), updated.BorderRadius );
		}
		finally
		{
			root.Delete( true );
		}
	}

	[TestMethod]
	public void TransformsFollowAncestorsAndLayerChanges()
	{
		var root = new RootPanel { PanelBounds = new Rect( 0, 0, 400, 300 ) };
		var parent = root.AddChild<Panel>();
		parent.Style.Set( "width: 100px; height: 100px; transform: translateX(20px);" );
		var child = parent.AddChild<Panel>();
		child.Style.Set( "width: 50px; height: 50px; background-color: red;" );
		try
		{
			root.Layout();
			var initial = ChildTransform();
			Assert.AreNotEqual( Matrix.Identity, initial );
			Assert.AreEqual( initial, ChildTransform() );
			parent.Style.Set( "transform: translateX(40px);" );
			root.Layout();
			var moved = ChildTransform();
			Assert.AreNotEqual( initial, moved );
			Assert.AreEqual( parent.RenderTransform, moved );

			// A layer doesn't change the child's transform; the painter cancels the layer's share on the target.
			parent.Style.Set( "isolation: isolate;" );
			root.Layout();
			Assert.IsTrue( parent.HasPanelLayer );
			Assert.AreEqual( moved, ChildTransform() );
			Assert.AreEqual( moved, ChildTransform() );
			parent.Style.Set( "isolation: auto;" );
			root.Layout();
			Assert.IsFalse( parent.HasPanelLayer );
			Assert.AreEqual( moved, ChildTransform() );

			child.Style.Set( "transform: rotate(30deg); transform-origin: 0% 0%;" );
			root.Layout();
			var rotated = ChildTransform();
			child.Style.Set( "transform-origin: 100% 100%;" );
			root.Layout();
			Assert.AreNotEqual( rotated, ChildTransform() );
		}
		finally
		{
			root.Delete( true );
		}

		Matrix ChildTransform()
		{
			var frame = PanelDrawSnapshot.Build( root );
			var box = frame.Instances.Single( x => x.Color == Color.Red );
			return frame.Transforms[box.TransformIndex].Mat;
		}
	}

	[TestMethod]
	public void GeometryFollowsStylesAndBounds()
	{
		var root = new RootPanel { PanelBounds = new Rect( 0, 0, 400, 300 ) };
		var panel = root.AddChild<Panel>();
		panel.Style.Set( "width: 100px; height: 80px; background-color: red; border: 4px solid green; border-radius: 25%; padding: 6px; background-clip: content-box; outline: 2px blue; outline-offset: 3px; overflow: hidden;" );

		try
		{
			Check( new Vector2( 25, 20 ), 4, 10, 2, 3 );
			panel.Style.Set( "border-width: 8px; border-radius: 20%; padding: 3px; outline-width: 5px; outline-offset: 1px;" );
			Check( new Vector2( 20, 16 ), 8, 11, 5, 1 );
			panel.Style.Set( "width: 200px; height: 120px;" );
			Check( new Vector2( 40, 24 ), 8, 11, 5, 1 );
		}
		finally
		{
			root.Delete( true );
		}

		void Check( Vector2 radius, float border, float inset, float outlineWidth, float outlineOffset )
		{
			root.Layout();
			var first = PanelDrawSnapshot.Build( root );
			var box = first.Instances.Single( x => x.Mode == 0 );
			Assert.AreEqual( new Vector4( radius.x ), box.BorderRadius );
			Assert.AreEqual( new Vector4( radius.y ), box.BorderRadiusV );
			Assert.AreEqual( new Vector4( border ), box.BorderSize );
			Assert.AreEqual( new Vector4( inset ), box.BackgroundClipRect );
			var outline = first.Instances.Single( x => x.Mode == 3 );
			Assert.AreEqual( outlineWidth, outline.BackgroundRect.z );
			Assert.AreEqual( outlineOffset, outline.BackgroundRect.w );
			CollectionAssert.AreEqual( first.Instances, PanelDrawSnapshot.Build( root ).Instances );

			using var painter = Painter.Begin( new CommandList(), root.PanelBounds );
			using var clip = panel.ClipChildren( painter, panel.Box.ClipRect );
			Assert.AreEqual( radius - new Vector2( border ), painter.DestinationClip.Clips[0].Radii.TopLeft );
		}
	}

	[TestMethod]
	public void RelativeRadiiFollowFontAndViewportChanges()
	{
		var root = new RootPanel { PanelBounds = new Rect( 0, 0, 400, 300 ) };
		root.Style.FontSize = 10;
		var panel = root.AddChild<Panel>();
		panel.Style.Set( "width: 100px; height: 80px; background-color: red; border-radius: calc(1em + 1vw);" );
		root.AddChild<Panel>().Style.Set( "width: 20px; height: 20px; font-size: 40px;" );

		try
		{
			Check( 14 );
			root.Style.FontSize = 20;
			Check( 24 );
			root.PanelBounds = new Rect( 0, 0, 800, 300 );
			Check( 28 );
		}
		finally
		{
			root.Delete( true );
		}

		void Check( float radius )
		{
			root.Layout();
			var box = PanelDrawSnapshot.Build( root ).Instances.Single();
			Assert.AreEqual( new Vector4( radius ), box.BorderRadius );
			Assert.AreEqual( new Vector4( radius ), box.BorderRadiusV );
		}
	}

	[TestMethod]
	public void DpiChangesRefreshRadiiAndImageSlices()
	{
		var root = new ScaledRoot { PanelBounds = new Rect( 0, 0, 400, 300 ) };
		var panel = root.AddChild<Panel>();
		panel.Style.Set( "width: 100px; height: 80px; background-color: red; border: 2px solid green; border-radius: 8px;" );
		panel.Style.BorderImageSource = Texture.White;
		panel.Style.BorderImageWidthLeft = Length.Percent( 10 );
		panel.Style.BorderImageWidthTop = Length.Percent( 10 );
		panel.Style.BorderImageWidthRight = Length.Percent( 10 );
		panel.Style.BorderImageWidthBottom = Length.Percent( 10 );

		try
		{
			root.Layout();
			var first = PanelDrawSnapshot.Build( root ).Instances.Single();
			Assert.AreEqual( new Vector4( 8 ), first.BorderRadius );
			Assert.AreEqual( new Vector4( 2 ), first.BorderSize );
			Assert.AreEqual( new Vector4( 9 ), first.BorderImageSlice );

			root.SetScale( 2 );
			root.Layout();
			var scaled = PanelDrawSnapshot.Build( root ).Instances.Single();
			Assert.AreEqual( new Vector4( 16 ), scaled.BorderRadius );
			Assert.AreEqual( new Vector4( 4 ), scaled.BorderSize );
			Assert.AreEqual( new Vector4( 18 ), scaled.BorderImageSlice );

			panel.Style.BorderImageSource = null;
			root.Layout();
			var plain = PanelDrawSnapshot.Build( root ).Instances.Single();
			Assert.AreEqual( 0, plain.BorderImageIndex );
			Assert.AreEqual( Vector4.Zero, plain.BorderImageSlice );
		}
		finally
		{
			root.Delete( true );
		}
	}
}
