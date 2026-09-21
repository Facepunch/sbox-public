using Sandbox.UI;
using System.Reflection;

namespace EngineTests;

[TestClass]
public class PanelBoxCacheTest
{
	[TestInitialize]
	public void RequireVulkan()
	{
		if ( g_pRenderDevice.GetRenderDeviceAPI() != NativeEngine.RenderDeviceAPI_t.RENDER_DEVICE_API_VULKAN )
			Assert.Inconclusive( "Requires the Vulkan renderer." );
	}

	[TestMethod]
	public void DeletingAPanelReleasesCachedTextures()
	{
		var root = new RootPanel { PanelBounds = new Rect( 0, 0, 400, 300 ) };
		var image = root.AddChild<Image>();
		image.Texture = Texture.White;
		image.Style.Set( "width: 100px; height: 80px; border: 3px solid green;" );
		image.Style.BackgroundImage = Texture.White;
		image.Style.BorderImageSource = Texture.White;
		try
		{
			root.Layout();
			Assert.IsNotNull( Descriptor( image ).BackgroundImage );
			Assert.IsNotNull( Descriptor( image ).BorderImage.Texture );

			image.Delete( true );

			Assert.IsNull( Descriptor( image ).BackgroundImage );
			Assert.IsNull( Descriptor( image ).BorderImage.Texture );
		}
		finally { root.Delete( true ); }
	}

	[TestMethod]
	[DataRow( ObjectFit.Contain, 80f, 80f )]
	[DataRow( ObjectFit.Cover, 100f, 100f )]
	[DataRow( ObjectFit.Fill, 100f, 80f )]
	public void DirectImageHonorsObjectFit( ObjectFit fit, float width, float height )
	{
		var root = new RootPanel { PanelBounds = new Rect( 0, 0, 400, 300 ) };
		var image = root.AddChild<Image>();
		image.Texture = Texture.White;
		image.Style.Set( "width: 100px; height: 80px;" );
		image.Style.ObjectFit = fit;
		try
		{
			root.Layout();
			var frame = PanelDrawSnapshot.Build( root );
			var box = frame.Instances.Single();
			Assert.AreEqual( width, box.BackgroundRect.z );
			Assert.AreEqual( height, box.BackgroundRect.w );
		}
		finally { root.Delete( true ); }
	}

	[TestMethod]
	public void ScenePanelDrawsItsRenderTextureWithoutPreparingContent()
	{
		var root = new RootPanel { PanelBounds = new Rect( 0, 0, 400, 300 ) };
		var scene = root.AddChild<ScenePanel>();
		scene.Style.Set( "width: 100px; height: 80px;" );
		try
		{
			root.Layout();
			var texture = Texture.CreateRenderTarget().WithSize( 16, 16 ).Create();
			typeof( ScenePanel ).GetProperty( nameof( ScenePanel.RenderTexture ) ).SetValue( scene, texture );
			var frame = PanelDrawSnapshot.Build( root );
			Assert.AreEqual( texture.Index, frame.Instances.Single().TextureIndex );
		}
		finally { root.Delete( true ); }
	}

	[TestMethod]
	public void SvgDrawsItsTextureWithoutPreparingContent()
	{
		var root = new RootPanel { PanelBounds = new Rect( 0, 0, 400, 300 ) };
		var svg = root.AddChild<SvgPanel>();
		svg.Style.Set( "width: 100px; height: 80px; border: 3px solid green;" );
		try
		{
			root.Layout();
			typeof( SvgPanel ).GetField( "texture", BindingFlags.Instance | BindingFlags.NonPublic ).SetValue( svg, Texture.White );
			var frame = PanelDrawSnapshot.Build( root );
			Assert.AreEqual( 2, frame.Instances.Length );
			Assert.AreEqual( Vector4.Zero, frame.Instances[1].BorderSize );
		}
		finally { root.Delete( true ); }
	}

	[TestMethod]
	public void DrawingOpacityDoesNotChangeCachedColors()
	{
		var root = new RootPanel { PanelBounds = new Rect( 0, 0, 400, 300 ) };
		var panel = root.AddChild<Panel>();
		panel.Style.Set( "width: 100px; height: 80px; opacity: 0.5; border: 4px solid green;" );
		panel.Style.BackgroundColor = Color.Red.WithAlpha( 0.6f );
		panel.Style.BorderColor = Color.Green.WithAlpha( 0.8f );
		try
		{
			root.Layout();
			var descriptor = Descriptor( panel );
			Assert.AreEqual( 0.6f, descriptor.Color.a, 0.001f );
			Assert.AreEqual( 0.8f, descriptor.Stroke.ColorL.a, 0.001f );

			foreach ( float opacity in new[] { 0.25f, 1f, 0.5f } )
			{
				root.BuildCommandList( opacity );
				var box = PanelDrawSnapshot.Read( root ).Instances.Single();
				Assert.AreEqual( 0.3f * opacity, box.Color.a, 0.001f );
				Assert.AreEqual( 0.4f * opacity, box.BorderColorL.a, 0.001f );
				Assert.AreEqual( descriptor, Descriptor( panel ) );
			}
		}
		finally { root.Delete( true ); }
	}

	sealed class MovingImage : Image
	{
		internal Vector2 Offset;

		public override void OnLayout( ref Rect rect )
		{
			rect.Position += Offset;
		}
	}

	[TestMethod]
	public void PositionOnlyLayoutMovesTheBackgroundAndDirectImage()
	{
		var root = new RootPanel { PanelBounds = new Rect( 0, 0, 400, 300 ) };
		var image = root.AddChild<MovingImage>();
		image.Texture = Texture.White;
		image.Style.Set( "position: absolute; left: 10px; top: 20px; width: 100px; height: 80px; background-color: red; border: 3px solid green;" );
		try
		{
			root.Layout();
			root.Layout();
			var background = Descriptor( image );
			image.Offset = new Vector2( 30, 17 );
			image.SetNeedsFinalLayout();
			root.Layout();

			Assert.AreEqual( background.Rect.Position + image.Offset, image.Box.Rect.Position );
			background.Rect = image.Box.Rect;
			Assert.AreEqual( background, Descriptor( image ) );
			var frame = PanelDrawSnapshot.Build( root );
			Assert.AreEqual( 2, frame.Instances.Length );
			Assert.AreEqual( image.Box.Rect.Left, frame.Instances[0].Rect.x );
			Assert.AreEqual( image.Box.Rect.Top, frame.Instances[0].Rect.y );
			Assert.AreEqual( 0f, frame.Instances[1].Rect.x, "Image uses local painter coordinates." );
			Assert.AreEqual( 0f, frame.Instances[1].Rect.y );
			Assert.AreEqual( Vector4.Zero, frame.Instances[1].BorderSize, "The image does not repaint CSS borders." );
		}
		finally { root.Delete( true ); }
	}

	[TestMethod]
	public void StyleAndSizeChangesResolveBorderShapes()
	{
		var root = new RootPanel { PanelBounds = new Rect( 0, 0, 400, 300 ) };
		var panel = root.AddChild<Panel>();
		panel.Style.Set( "width: 100px; height: 80px; background-color: red; border: 2px solid green; border-shape: polygon(0% 0%, 100% 0%, 50% 100%);" );
		try
		{
			root.Layout();
			Assert.AreEqual( new Vector4( 0, 0, 100, 0 ), Descriptor( panel ).BorderShapeData.Polygon01 );
			Assert.AreEqual( new Vector4( 50, 80, 0, 0 ), Descriptor( panel ).BorderShapeData.Polygon23 );
			PanelDrawSnapshot.Build( root );
			panel.Style.Set( "width: 200px; height: 120px; border-width: 6px; background-color: blue;" );
			root.Layout();
			var descriptor = Descriptor( panel );
			Assert.AreEqual( new Vector4( 0, 0, 200, 0 ), descriptor.BorderShapeData.Polygon01 );
			Assert.AreEqual( new Vector4( 100, 120, 0, 0 ), descriptor.BorderShapeData.Polygon23 );
			Assert.AreEqual( new Vector4( 6 ), descriptor.Stroke.Size );
			Assert.AreEqual( Color.Blue, descriptor.Color );
			var frame = PanelDrawSnapshot.Build( root );
			Assert.AreEqual( descriptor.BorderShapeData, frame.Shapes[frame.Instances.Single().ShapeIndex] );

			panel.Style.Set( "border-shape: none; border: none;" );
			root.Layout();
			Assert.IsFalse( Descriptor( panel ).HasBorderShape );
			Assert.AreEqual( Vector4.Zero, Descriptor( panel ).Stroke.Size );
			Assert.AreEqual( -1, PanelDrawSnapshot.Build( root ).Instances.Single().ShapeIndex );
		}
		finally { root.Delete( true ); }
	}

	[TestMethod]
	public void TextMasksDoNotChangeTheCachedBox()
	{
		var root = new RootPanel { PanelBounds = new Rect( 0, 0, 400, 300 ) };
		var panel = root.AddChild<Panel>();
		panel.Style.Set( "width: 200px; height: 100px; background: linear-gradient(red, blue); border: 3px solid green; background-clip: text;" );
		var first = panel.AddChild<Label>();
		first.Style.Set( "display: block; font-family: Arial; font-size: 20px;" );
		first.Text = "First";
		var second = panel.AddChild<Label>();
		second.Style.Set( "display: block; font-family: Arial; font-size: 20px;" );
		second.Text = "Second";
		try
		{
			root.Layout();
			var descriptor = Descriptor( panel );
			for ( int frame = 0; frame < 2; frame++ )
			{
				var boxes = PanelDrawSnapshot.Build( root ).Instances;
				Assert.AreEqual( 1, boxes.Count( x => x.BorderSize != Vector4.Zero ) );
				Assert.AreEqual( 2, boxes.Count( x => x.BackgroundClip == (int)BackgroundClip.Text ) );
				Assert.AreEqual( descriptor, Descriptor( panel ) );
				Assert.IsNull( Descriptor( panel ).TextMask );
			}

			panel.Style.Set( "background-clip: content-box; padding: 7px;" );
			root.Layout();
			Assert.AreEqual( new Vector4( 10 ), Descriptor( panel ).BackgroundClipInset );
			Assert.AreEqual( BackgroundClip.ContentBox, Descriptor( panel ).BackgroundClip );
			Assert.IsFalse( PanelDrawSnapshot.Build( root ).Instances.Any( x => x.BackgroundClip == (int)BackgroundClip.Text ) );
		}
		finally { root.Delete( true ); }
	}

	static Painter.BoxDescriptor Descriptor( Panel panel )
	{
		var cache = typeof( Panel ).GetField( "_paintCache", BindingFlags.Instance | BindingFlags.NonPublic ).GetValue( panel );
		var image = cache.GetType().GetField( "Background", BindingFlags.Instance | BindingFlags.NonPublic ).GetValue( cache );
		return (Painter.BoxDescriptor)image.GetType().GetField( "Descriptor", BindingFlags.Instance | BindingFlags.NonPublic ).GetValue( image );
	}
}
