using Sandbox.Rendering;
using Sandbox.Rendering.Bridge;
using Borealis;

namespace SceneTests;

/// <summary>
/// Checks camera fog snapshots at the managed renderer bridge without component or volume resolution.
/// </summary>
[TestClass]
public class ManagedCameraFogTests
{
	/// <summary>
	/// A reused render view copies the current camera's fog and clears the preceding camera's override.
	/// </summary>
	[TestMethod]
	public void BridgeCopiesAndClearsFogSnapshots()
	{
		using var first = new SceneCamera { Fog = new FogSetup { Color = Color.Red } };
		using var second = new SceneCamera();
		var view = new RenderView();
		var size = new Vector2( 1280, 720 );

		SceneBridge.FillView( view, first, new ViewSetup(), size );
		Assert.AreEqual( first.Fog.ToBorealis(), view.Fog );
		first.Fog = new FogSetup { Color = Color.Blue };
		Assert.AreEqual( Color.Red, view.Fog.Value.Color, "A prepared view holds its own value snapshot" );

		SceneBridge.FillView( view, second, new ViewSetup(), size );
		Assert.IsNull( view.Fog, "A camera inheriting legacy fog clears the preceding override" );
		Assert.AreEqual( Color.Blue, first.Fog.Value.Color );

		SceneBridge.FillView( view, first, new ViewSetup(), size );
		Assert.AreEqual( Color.Blue, view.Fog.Value.Color );
		first.EnablePostProcessing = false;
		SceneBridge.FillView( view, first, new ViewSetup(), size );
		Assert.IsNull( view.Fog );
	}
}
