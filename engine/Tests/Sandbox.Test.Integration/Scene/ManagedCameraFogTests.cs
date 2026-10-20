using Sandbox.Rendering;
using Sandbox.SceneRenderer;
using System.Reflection;

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
		var bridge = typeof( RenderView ).Assembly.GetType( "Sandbox.SceneRenderer.Bridge.SceneBridge", true );
		var fillView = bridge.GetMethod( "FillView", BindingFlags.Static | BindingFlags.NonPublic );

		fillView.Invoke( null, [view, first, new Vector2( 1280, 720 )] );
		Assert.AreEqual( first.Fog, view.Fog );
		first.Fog = new FogSetup { Color = Color.Blue };
		Assert.AreEqual( Color.Red, view.Fog.Value.Color, "A prepared view holds its own value snapshot" );

		fillView.Invoke( null, [view, second, new Vector2( 1280, 720 )] );
		Assert.IsNull( view.Fog, "A camera inheriting legacy fog clears the preceding override" );
		Assert.AreEqual( Color.Blue, first.Fog.Value.Color );

		fillView.Invoke( null, [view, first, new Vector2( 1280, 720 )] );
		Assert.AreEqual( Color.Blue, view.Fog.Value.Color );
		first.EnablePostProcessing = false;
		fillView.Invoke( null, [view, first, new Vector2( 1280, 720 )] );
		Assert.IsNull( view.Fog );
	}
}
