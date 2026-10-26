namespace SceneTests.Components;

/// <summary>
/// Camera preparation must publish current modulation data even when the scene has never ticked.
/// </summary>
[TestClass]
public class LightModulatorRenderPreparationTests
{
	/// <summary>
	/// Infinite planes reach distant lights without making UV edits or reference sizes rebuild candidates.
	/// </summary>
	[TestMethod]
	public void InfinitePlaneAndWorldSizedUVs()
	{
		if ( !Graphics.IsAvailable )
			Assert.Inconclusive( "Requires SBOX_TEST_GRAPHICS=1." );

		var scene = Scene.CreateEditorScene();
		using var scope = scene.Push();
		try
		{
			var light = scene.CreateObject().Components.Create<PointLight>();
			light.WorldPosition = new Vector3( 0, 1000, 0 );
			light.Radius = 10;
			light.Shadows = false;
			var pane = scene.CreateObject().Components.Create<LightModulator>();
			pane.Texture = Texture.White;
			pane.WorldPosition = new Vector3( 5, 0, 0 );
			var system = scene.GetSystem<LightModulatorSystem>();
			scene.PreCameraRender();
			Assert.AreEqual( 0, system.CandidateCount );
			var rebuilds = system.RebuildCount;
			var uploads = system.RectangleUploadCount;

			pane.WorldSizedUVs = true;
			scene.PreCameraRender();
			Assert.AreEqual( rebuilds, system.RebuildCount, "UV units must not change the physical bounds." );
			Assert.AreEqual( ++uploads, system.RectangleUploadCount );
			Assert.IsTrue( pane.BuildData( out var finite ) );
			Assert.AreEqual( 1, finite.WorldSizedUVs );
			Assert.AreEqual( 0, finite.Infinite );

			pane.Infinite = true;
			scene.PreCameraRender();
			Assert.AreEqual( ++rebuilds, system.RebuildCount );
			Assert.AreEqual( 1, system.CandidateCount, "The plane must reach lights far outside its reference rectangle." );
			pane.Size = Vector2.Zero;
			pane.WorldScale = new Vector3( 1, 3, 2 );
			scene.PreCameraRender();
			Assert.AreEqual( rebuilds, system.RebuildCount );
			Assert.AreEqual( 1, system.CandidateCount, "An infinite world-mapped plane does not depend on Size." );
			Assert.IsTrue( pane.BuildData( out var infinite ) );
			Assert.AreEqual( 1, infinite.Infinite );

			pane.Size = new Vector2( 64, 96 );
			pane.WorldSizedUVs = false;
			scene.PreCameraRender();
			Assert.AreEqual( rebuilds, system.RebuildCount, "Changing an infinite plane's UV reference must not rebuild candidates." );
			pane.WorldRotation = Rotation.FromYaw( 90 );
			scene.PreCameraRender();
			Assert.AreEqual( ++rebuilds, system.RebuildCount );
			Assert.AreEqual( 0, system.CandidateCount, "Rotating the infinite plane away must exclude the light." );
			pane.Infinite = false;
			scene.PreCameraRender();
			Assert.AreEqual( ++rebuilds, system.RebuildCount, "Switching back to a rectangle must rebuild candidates." );
		}
		finally
		{
			scene.Destroy();
		}
	}

	/// <summary>
	/// Named controls preserve the shader defaults and retain independent values when switching effects.
	/// </summary>
	[TestMethod]
	public void EffectControlsPackAndRetainSettings()
	{
		if ( !Graphics.IsAvailable )
			Assert.Inconclusive( "Requires SBOX_TEST_GRAPHICS=1." );

		var scene = Scene.CreateEditorScene();
		using var scope = scene.Push();
		try
		{
			var pane = scene.CreateObject().Components.Create<LightModulator>();
			pane.Texture = Texture.White;
			foreach ( var effect in System.Enum.GetValues<LightModulator.ModulationEffect>() )
			{
				pane.Effect = effect;
				Assert.IsTrue( pane.BuildData( out var defaults ) );
				Assert.AreEqual( new Vector4( 1, 1, 1, 0 ), defaults.EffectParameters, $"{effect} defaults must preserve its appearance." );
			}

			pane.Effect = LightModulator.ModulationEffect.Caustics;
			pane.CausticsWobble = 0.05f;
			pane.CausticsLayerScale = 2.26f;
			scene.PreCameraRender();
			var system = scene.GetSystem<LightModulatorSystem>();
			var uploads = system.RectangleUploadCount;

			pane.CloudShadowDepth = 0.4f;
			pane.ScrollSpeed = Vector2.One;
			scene.PreCameraRender();
			Assert.AreEqual( uploads, system.RectangleUploadCount, "Inactive effect edits must not upload unchanged rendering data." );
			pane.Effect = LightModulator.ModulationEffect.CloudShadows;
			Assert.IsTrue( pane.BuildData( out var cloud ) );
			Assert.AreEqual( 0.4f, cloud.EffectParameters.y );
			pane.Effect = LightModulator.ModulationEffect.Caustics;
			Assert.IsTrue( pane.BuildData( out var caustics ) );
			Assert.AreEqual( new Vector4( 1, 2, 2, 0 ), caustics.EffectParameters );
			Assert.AreEqual( Vector2.Zero, caustics.ScrollSpeed, "Other effects must not inherit scrolling." );
			pane.Effect = LightModulator.ModulationEffect.Scroll;
			Assert.IsTrue( pane.BuildData( out var scroll ) );
			Assert.AreEqual( Vector2.One, scroll.ScrollSpeed, "Scroll retains its own two-axis speed." );

			pane.Effect = LightModulator.ModulationEffect.Kaleidoscope;
			pane.KaleidoscopeSegments = 12;
			pane.KaleidoscopeSpeed = -72;
			Assert.IsTrue( pane.BuildData( out var kaleidoscope ) );
			Assert.AreEqual( new Vector4( -2, 1, 2, 0 ), kaleidoscope.EffectParameters );

			pane.Effect = LightModulator.ModulationEffect.Ripple;
			pane.RippleSpeed = float.NaN;
			pane.RippleDisplacement = float.PositiveInfinity;
			pane.RippleFrequency = 0;
			pane.RippleTimeOffset = 1000;
			Assert.IsTrue( pane.BuildData( out var ripple ) );
			Assert.AreEqual( new Vector4( 1, 1, 0.1f, 60 ), ripple.EffectParameters, "Scripted values must remain finite and within the control ranges." );
		}
		finally
		{
			scene.Destroy();
		}
	}

	/// <summary>
	/// Small changes to reciprocal axes can move a large pane into a light's range.
	/// </summary>
	[TestMethod]
	public void SmallRotationRefreshesCandidates()
	{
		if ( !Graphics.IsAvailable )
			Assert.Inconclusive( "Requires SBOX_TEST_GRAPHICS=1." );

		var scene = Scene.CreateEditorScene();
		using var scope = scene.Push();

		try
		{
			var light = new ScenePointLight( scene.SceneWorld, new Vector3( 42, 499, 0 ), 1, Color.White );
			var pane = scene.CreateObject().Components.Create<LightModulator>();
			pane.Texture = Texture.White;
			pane.Size = new Vector2( 1000, 1000 );
			pane.WorldPosition = new Vector3( 50, 0, 0 );

			var system = scene.GetSystem<LightModulatorSystem>();
			scene.PreCameraRender();
			Assert.AreEqual( 0, system.CandidateCount );

			pane.WorldRotation = Rotation.FromYaw( 1 );
			scene.PreCameraRender();
			Assert.AreEqual( 1, system.CandidateCount, "A small rotation must include lights newly intersecting the pane." );
		}
		finally
		{
			scene.Destroy();
		}
	}

	/// <summary>
	/// Appearance edits upload rectangles without rebuilding candidates; light changes do the reverse.
	/// </summary>
	[TestMethod]
	public void AppearanceChangesReuseCandidates()
	{
		if ( !Graphics.IsAvailable )
			Assert.Inconclusive( "Requires SBOX_TEST_GRAPHICS=1." );

		var scene = Scene.CreateEditorScene();
		using var scope = scene.Push();

		try
		{
			var light = scene.CreateObject().Components.Create<PointLight>();
			light.Radius = 100;
			light.Shadows = false;

			var pane = scene.CreateObject().Components.Create<LightModulator>();
			pane.Texture = Texture.White;
			pane.WorldPosition = new Vector3( 50, 0, 0 );

			var system = scene.GetSystem<LightModulatorSystem>();
			scene.PreCameraRender();
			Assert.AreEqual( 1, system.CandidateCount );
			Assert.AreEqual( 1, system.RectangleUploadCount );
			var rebuildCount = system.RebuildCount;
			var uploadCount = system.RectangleUploadCount;
			var buildCount = pane.DataBuildCount;

			System.Action[] appearanceChanges =
			[
				() => pane.Tint = Color.Red,
				() => pane.Brightness = 2,
				() => pane.Tiling = new Vector2( 2, 3 ),
				() => pane.Offset = new Vector2( 0.25f, 0.5f ),
				() => pane.TextureRotation = 45,
				() => pane.Effect = LightModulator.ModulationEffect.Scroll,
				() => pane.ScrollSpeed = Vector2.One,
				() => pane.Effect = LightModulator.ModulationEffect.Caustics,
				() => pane.CausticsSpeed = 0.5f,
				() => pane.CausticsWobble = 0.03f,
				() => pane.CausticsLayerScale = 1.356f,
				() => pane.CausticsTimeOffset = 0.25f,
				() => pane.Effect = LightModulator.ModulationEffect.CloudShadows,
				() => pane.CloudShadowSpeed = 0.5f,
				() => pane.CloudShadowDepth = 0.5f,
				() => pane.CloudShadowLayerScale = 2.04f,
				() => pane.CloudShadowTimeOffset = 0.25f,
				() => pane.Effect = LightModulator.ModulationEffect.Rotate,
				() => pane.RotationSpeed = 43.2f,
				() => pane.RotationTimeOffset = 0.25f,
				() => pane.Effect = LightModulator.ModulationEffect.Kaleidoscope,
				() => pane.KaleidoscopeSpeed = 43.2f,
				() => pane.KaleidoscopeBlend = 0.5f,
				() => pane.KaleidoscopeSegments = 8,
				() => pane.KaleidoscopeTimeOffset = 0.25f,
				() => pane.Effect = LightModulator.ModulationEffect.ChromaticSplit,
				() => pane.ChromaticSplitSpeed = 0.5f,
				() => pane.ChromaticSplitSeparation = 0.012f,
				() => pane.ChromaticSplitTimeOffset = 0.25f,
				() => pane.Effect = LightModulator.ModulationEffect.Ripple,
				() => pane.RippleSpeed = 0.5f,
				() => pane.RippleDisplacement = 0.03f,
				() => pane.RippleFrequency = 0.5f,
				() => pane.RippleTimeOffset = 0.25f,
				() => pane.Effect = LightModulator.ModulationEffect.Pulse,
				() => pane.PulseSpeed = 0.5f,
				() => pane.PulseDepth = 0.5f,
				() => pane.PulseSharpness = 0.5f,
				() => pane.PulseTimeOffset = 0.25f,
				() => pane.Strength = 0,
				() => pane.Texture = Texture.Black
			];

			foreach ( var change in appearanceChanges )
			{
				change();
				scene.PreCameraRender();
				Assert.AreEqual( rebuildCount, system.RebuildCount, "Appearance must not rebuild candidate lists." );
				Assert.AreEqual( ++uploadCount, system.RectangleUploadCount, "Changed appearance must reach the GPU." );
				Assert.AreEqual( ++buildCount, pane.DataBuildCount );
				Assert.AreEqual( 1, system.CandidateCount );

				scene.PreCameraRender();
				Assert.AreEqual( uploadCount, system.RectangleUploadCount, "Unchanged captures must reuse the upload." );
				Assert.AreEqual( buildCount, pane.DataBuildCount, "Unchanged captures must not repack rectangles." );
			}

			light.Radius = 25;
			scene.PreCameraRender();
			Assert.AreEqual( ++rebuildCount, system.RebuildCount );
			Assert.AreEqual( 0, system.CandidateCount );
			Assert.AreEqual( uploadCount, system.RectangleUploadCount, "Light changes must not upload unchanged rectangles." );

			pane.WorldPosition = new Vector3( 10, 0, 0 );
			scene.PreCameraRender();
			Assert.AreEqual( ++rebuildCount, system.RebuildCount );
			Assert.AreEqual( ++uploadCount, system.RectangleUploadCount );
			Assert.AreEqual( 1, system.CandidateCount );

			pane.Size = new Vector2( 128, 128 );
			scene.PreCameraRender();
			Assert.AreEqual( ++rebuildCount, system.RebuildCount, "Resizing must invalidate the broad phase." );
			Assert.AreEqual( ++uploadCount, system.RectangleUploadCount );
		}
		finally
		{
			scene.Destroy();
		}
	}

	/// <summary>
	/// Unticked scenes, same-frame changes and repeated captures use the current cached snapshot.
	/// </summary>
	[TestMethod]
	public void PrepareUntickedSceneAndImmediateChanges()
	{
		if ( !Graphics.IsAvailable )
			Assert.Inconclusive( "Requires SBOX_TEST_GRAPHICS=1." );

		var scene = Scene.CreateEditorScene();
		using var scope = scene.Push();

		try
		{
			var light = scene.CreateObject().Components.Create<PointLight>();
			light.Radius = 100;
			light.Shadows = false;

			// Sunlight uses the scene rectangle buffer without adding local-light candidates.
			var sun = scene.CreateObject().Components.Create<DirectionalLight>();
			sun.Shadows = false;

			var pane = scene.CreateObject().Components.Create<LightModulator>();
			pane.Texture = Texture.White;
			pane.WorldPosition = new Vector3( 50, 0, 0 );

			var camera = scene.CreateObject().Components.Create<CameraComponent>();
			var system = scene.GetSystem<LightModulatorSystem>();
			Assert.IsNotNull( system );
			Assert.AreEqual( 0, system.RebuildCount );
			scene.Signal( GameObjectSystem.Stage.FinishUpdate );
			Assert.AreEqual( 0, system.RebuildCount, "Simulation must not perform a second rendering refresh." );
			Assert.AreEqual( 0, pane.DataBuildCount );

			// No EditorTick: this is the preparation path used by on-demand captures.
			scene.PreCameraRender();
			Assert.AreEqual( 1, system.CandidateCount );
			Assert.AreEqual( 1, camera.SceneCamera.Attributes.GetInt( "LightModulatorRectangleCount" ) );
			Assert.AreEqual( 1, camera.SceneCamera.Attributes.GetInt( "LightModulatorCandidateCount" ) );

			var rebuildCount = system.RebuildCount;
			scene.PreCameraRender();
			Assert.AreEqual( rebuildCount, system.RebuildCount, "An unchanged capture must reuse the GPU snapshot." );
			Assert.AreEqual( 1, pane.DataBuildCount );

			pane.WorldPosition = new Vector3( 200, 0, 0 );
			scene.PreCameraRender();
			Assert.AreEqual( 0, system.CandidateCount );
			Assert.AreEqual( 0, camera.SceneCamera.Attributes.GetInt( "LightModulatorCandidateCount" ) );
			Assert.AreEqual( 1, camera.SceneCamera.Attributes.GetInt( "LightModulatorRectangleCount" ),
				"Sunlight must retain rectangles outside every local light's range." );

			pane.WorldPosition = new Vector3( 50, 0, 0 );
			scene.PreCameraRender();
			Assert.AreEqual( 1, system.CandidateCount );

			pane.Enabled = false;
			scene.PreCameraRender();
			Assert.AreEqual( 0, system.CandidateCount );
			Assert.AreEqual( 0, camera.SceneCamera.Attributes.GetInt( "LightModulatorRectangleCount" ) );
			Assert.AreEqual( 0, camera.SceneCamera.Attributes.GetInt( "LightModulatorCandidateCount" ) );

			pane.Enabled = true;
			scene.PreCameraRender();
			Assert.AreEqual( 1, system.CandidateCount );
		}
		finally
		{
			scene.Destroy();
		}
	}

	/// <summary>
	/// Parent transforms, texture handle replacement and raw scene-light deletion invalidate cached rendering data.
	/// </summary>
	[TestMethod]
	public void CachedDataTracksExternalChanges()
	{
		if ( !Graphics.IsAvailable )
			Assert.Inconclusive( "Requires SBOX_TEST_GRAPHICS=1." );

		var scene = Scene.CreateEditorScene();
		using var scope = scene.Push();
		using var texture = Texture.Create( 1, 1 ).WithData( new byte[] { 255, 255, 255, 255 } ).Finish();

		try
		{
			var light = new ScenePointLight( scene.SceneWorld, Vector3.Zero, 100, Color.White );
			var parent = scene.CreateObject();
			var paneObject = scene.CreateObject();
			paneObject.Parent = parent;
			var pane = paneObject.Components.Create<LightModulator>();
			pane.Texture = texture;
			pane.WorldPosition = new Vector3( 50, 0, 0 );
			var camera = scene.CreateObject().Components.Create<CameraComponent>();
			var system = scene.GetSystem<LightModulatorSystem>();

			using var hook = scene.AddHook( GameObjectSystem.Stage.PrepareRender, 0,
				() => scene.RenderAttributes.Set( "RenderPreparationTest", 42 ), nameof( LightModulatorRenderPreparationTests ), "Test preparation" );
			scene.PreCameraRender();
			Assert.AreEqual( 42, camera.SceneCamera.Attributes.GetInt( "RenderPreparationTest" ), "Preparation precedes camera attribute copying." );
			Assert.AreEqual( 1, system.CandidateCount );

			var buildCount = pane.DataBuildCount;
			var rebuildCount = system.RebuildCount;
			texture.CopyFrom( Texture.Black );
			scene.PreCameraRender();
			Assert.AreEqual( ++buildCount, pane.DataBuildCount, "Replacing a texture handle must refresh its bindless index." );
			Assert.AreEqual( rebuildCount, system.RebuildCount );

			parent.WorldPosition = new Vector3( 200, 0, 0 );
			scene.PreCameraRender();
			Assert.AreEqual( ++buildCount, pane.DataBuildCount );
			Assert.AreEqual( 0, system.CandidateCount );

			light.Position = pane.WorldPosition;
			scene.PreCameraRender();
			Assert.AreEqual( 1, system.CandidateCount );
			Assert.AreEqual( buildCount, pane.DataBuildCount );

			light.Delete();
			scene.PreCameraRender();
			Assert.AreEqual( 0, system.CandidateCount, "Deferred native deletion must not leave a light in the snapshot." );

			scene.ReloadSystems();
			scene.PreCameraRender();
			Assert.AreEqual( 1, camera.SceneCamera.Attributes.GetInt( "LightModulatorRectangleCount" ) );
			Assert.AreEqual( buildCount, pane.DataBuildCount, "Reloaded systems can reuse existing component snapshots." );
		}
		finally
		{
			scene.Destroy();
		}
	}
}
