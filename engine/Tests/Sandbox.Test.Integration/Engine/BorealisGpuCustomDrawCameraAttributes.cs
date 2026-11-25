using Borealis;
using Borealis.Backend;
using GpuViewConstants = Borealis.Gpu.ViewConstants;

namespace EngineTests;

/// <summary>
/// Custom objects on a Borealis.Gpu device read their camera's attributes (<c>SceneCamera.Attributes</c>, what
/// <c>Scene.RenderAttributes</c> set) in depth alone as in colour, as native's layers give a view's custom objects them.
/// </summary>
[TestClass]
public class BorealisGpuCustomDrawCameraAttributesTest
{
	/// <summary>
	/// Mouse Fishing's clutter grass sways by a time its game sets on the scene's attributes: drawn with it in colour and
	/// without it in the depth prepass, its blades failed the depth test against their own unswayed depth, and the clear
	/// colour showed through where they stood (blue grass).
	/// </summary>
	[TestMethod]
	public void DepthAloneReadsTheCamerasAttributesAsColourDoes()
	{
		TestGraphics.Require();
		using var borealis = BorealisGpuRenderBackend.Create( validation: true );

		const int size = 16;
		using var color = borealis.Services.CreateTexture( TextureDesc.Target( size, size, TextureFormat.RGBA16161616F ), "custom draw colour" );
		using var depth = borealis.Services.CreateTexture( TextureDesc.Target( size, size, TextureFormat.D32FS8 ), "custom draw depth" );
		using var context = new BackendContext( borealis.Services, null );
		context.Begin();

		var camera = RenderAttributes.CreateManaged();
		camera.Set( "GlobalTime", 34.25f );

		using var frame = new BorealisGpuFrame( borealis );
		using var view = new BorealisGpuView( borealis );
		var constants = new byte[System.Runtime.CompilerServices.Unsafe.SizeOf<GpuViewConstants>()];
		var depthAlone = new StageTarget
		{
			Context = context,
			Attributes = context.Attributes,
			Depth = depth,
			Size = new Vector2( size, size ),
			Viewport = new Rect( 0, 0, size, size ),
			MaxZ = 1,
			Samples = 1,
		};
		var withColour = new StageTarget
		{
			Context = context,
			Attributes = context.Attributes,
			Color = color,
			Depth = depth,
			Size = new Vector2( size, size ),
			Viewport = new Rect( 0, 0, size, size ),
			MaxZ = 1,
			ColorFormat = TextureFormat.RGBA16161616F,
			Samples = 1,
		};

		float inDepth = -1, inColour = -1;
		frame.RunInFrame( depthAlone, constants, view, first: false, () => inDepth = Graphics.Attributes.GetFloat( "GlobalTime" ), depthAlone: true, camera );
		frame.RunInFrame( withColour, constants, view, first: false, () => inColour = Graphics.Attributes.GetFloat( "GlobalTime" ), cameraAttributes: camera );
		context.Submit();
		borealis.Device.WaitIdle();

		Assert.AreEqual( 34.25f, inColour, "in colour, the camera's time" );
		Assert.AreEqual( 34.25f, inDepth, "in depth alone, the same time" );
		Assert.AreEqual( 0, borealis.ValidationErrors );
	}
}
