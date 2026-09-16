using Sandbox.UI;

namespace EngineTests;

[TestClass]
public class PanelTextureShapeTest
{
	[TestMethod]
	[DataRow( "circle(25% at 40% 60%)" )]
	[DataRow( "polygon(50% 0%, 100% 100%, 0% 100%)" )]
	[DataRow( "none" )]
	public void ImageTextureUsesTheSameShapeAsItsBackground( string shape )
	{
		if ( g_pRenderDevice.GetRenderDeviceAPI() != NativeEngine.RenderDeviceAPI_t.RENDER_DEVICE_API_VULKAN )
			Assert.Inconclusive( "Requires the Vulkan renderer." );

		var root = new RootPanel { PanelBounds = new Rect( 0, 0, 200, 200 ) };
		var image = root.AddChild<Image>();
		image.Texture = Texture.White;
		image.Style.Set( $"width: 100px; height: 100px; background-color: red; border-shape: {shape};" );
		try
		{
			root.Layout();
			var snapshot = PanelDrawSnapshot.Build( root );
			Assert.AreEqual( 2, snapshot.Instances.Length );
			var background = snapshot.Instances.Single( x => x.TextureIndex == 0 );
			var texture = snapshot.Instances.Single( x => x.TextureIndex == Texture.White.Index );
			Assert.AreEqual( shape == "none", background.ShapeIndex < 0 );
			Assert.AreEqual( background.ShapeIndex, texture.ShapeIndex );
		}
		finally
		{
			root.Delete( true );
		}
	}
}
