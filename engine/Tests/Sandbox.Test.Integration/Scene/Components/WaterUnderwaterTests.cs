using Sandbox.Volumes;

namespace SceneTests.Components;

/// <summary>
/// Exercises underwater appearance through native per-camera fog and post-process volume selection.
/// </summary>
[TestClass, DoNotParallelize]
public class WaterUnderwaterTests
{
	bool _previousPostProcessing;

	/// <summary>
	/// Keep the host's post-processing preference from changing the feature under test.
	/// </summary>
	[TestInitialize]
	public void Initialize()
	{
		_previousPostProcessing = PostProcessSystem.EnablePostProcess;
		PostProcessSystem.EnablePostProcess = true;
	}

	/// <summary>
	/// Restore the process-wide preference.
	/// </summary>
	[TestCleanup]
	public void Cleanup()
	{
		PostProcessSystem.EnablePostProcess = _previousPostProcessing;
	}

	/// <summary>
	/// Above and below cameras select fog independently, including secondary editor cameras.
	/// Moving a camera out of the water clears its native fog state.
	/// </summary>
	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void UnderwaterFogIsSelectedPerCamera( bool editor )
	{
		if ( !Graphics.IsAvailable )
			Assert.Inconclusive( "Requires SBOX_TEST_GRAPHICS=1." );

		var scene = editor ? Scene.CreateEditorScene() : new Scene();
		using var scope = scene.Push();
		try
		{
			var above = scene.CreateObject().Components.Create<CameraComponent>();
			above.WorldPosition = Vector3.Up * 32;
			var below = scene.CreateObject().Components.Create<CameraComponent>();
			below.WorldPosition = Vector3.Down * 32;
			var water = scene.CreateObject().Components.Create<WaterBody>();
			water.OverrideUnderwaterFogColor = true;
			water.UnderwaterFogColor = Color.Red;
			water.UnderwaterFogNearDistance = 12;
			water.UnderwaterFogAttenuationDistance = 96;
			water.UnderwaterFogMaximumOpacity = 0.8f;
			water.Effects = false;
			Tick( scene );
			above.CopyPostProcessing( above.SceneCamera );
			below.CopyPostProcessing( below.SceneCamera );
			Assert.IsNull( above.SceneCamera.Fog );
			Assert.IsNotNull( below.SceneCamera.Fog );
			var fog = below.SceneCamera.Fog.Value;
			Assert.AreEqual( Color.Red, fog.Color );
			Assert.AreEqual( 12f, fog.StartDistance );
			Assert.AreEqual( 96f, fog.AttenuationDistance );
			Assert.AreEqual( 0.8f, fog.MaximumOpacity );
			Assert.AreEqual( 1f, fog.Weight );

			below.WorldPosition = Vector3.Up * 32;
			below.CopyPostProcessing( below.SceneCamera );
			Assert.IsNull( below.SceneCamera.Fog );
			above.WorldPosition = Vector3.Down * 32;
			above.CopyPostProcessing( above.SceneCamera );
			Assert.AreEqual( Color.Red, above.SceneCamera.Fog.Value.Color );
		}
		finally
		{
			scene.Destroy();
		}
	}

	/// <summary>
	/// Refresh owned effects through the ordinary scene lifecycle.
	/// </summary>
	static void Tick( Scene scene )
	{
		if ( scene.IsEditor )
			scene.EditorTick( 1, 1f / 60 );
		else
			scene.GameTick( 1.0 / 50 );
	}
}
