using Sandbox.Rendering;

namespace EngineTests;

[TestClass]
public class InsertedPanelPaintingTest
{
	[TestMethod]
	public void InsertedPanelsRefreshTheirBuffersAfterRecordingAgain()
	{
		if ( g_pRenderDevice.GetRenderDeviceAPI() != NativeEngine.RenderDeviceAPI_t.RENDER_DEVICE_API_VULKAN )
			Assert.Inconclusive( "Requires Vulkan rendering." );

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
					scope.Context.SetViewport( bounds );
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
}
