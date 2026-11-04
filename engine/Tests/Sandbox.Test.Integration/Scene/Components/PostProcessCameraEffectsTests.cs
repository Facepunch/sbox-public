using System;
using Sandbox.Volumes;

namespace SceneTests.Components;

/// <summary>
/// Exercises the ordinary weighted-value API while writing camera state instead of drawing a fullscreen pass.
/// </summary>
public sealed class CameraStateTestEffect : BasePostProcess<CameraStateTestEffect>
{
	/// <inheritdoc />
	public override bool IsCameraEffect => true;

	/// <summary>
	/// Grayscale background intensity authored by this component.
	/// </summary>
	public float Value { get; set; }

	/// <summary>
	/// Number of builds performed by this component.
	/// </summary>
	public int RenderCalls { get; private set; }

	/// <summary>
	/// Optional nested capture performed before reading the weighted values and writing camera state.
	/// </summary>
	public Action<CameraComponent, SceneCamera> BeforeApply { get; set; }

	/// <inheritdoc />
	public override void Render()
	{
		RenderCalls++;
		BeforeApply?.Invoke( Camera, TargetCamera );
		var value = GetWeighted( x => x.Value );
		TargetCamera.BackgroundColor = new Color( value, value, value );
		TargetCamera.Tonemap.ExposureCompensation = value;
	}
}

/// <summary>
/// Records the historical scene-stage build cadence of an ordinary post-process effect.
/// </summary>
public sealed class OrdinaryPostProcessTestEffect : BasePostProcess<OrdinaryPostProcessTestEffect>
{
	/// <summary>
	/// Number of calls through the existing post-process build path.
	/// </summary>
	public int RenderCalls { get; private set; }

	/// <inheritdoc />
	public override void Render()
	{
		RenderCalls++;
	}
}

/// <summary>
/// Verifies that camera-state effects reuse the existing effect API without changing ordinary effect builds.
/// </summary>
[TestClass]
public class PostProcessCameraEffectsTests
{
	bool previousPostProcessing;

	/// <summary>
	/// Enables effect processing for each test.
	/// </summary>
	[TestInitialize]
	public void Initialize()
	{
		previousPostProcessing = PostProcessSystem.EnablePostProcess;
		PostProcessSystem.EnablePostProcess = true;
	}

	/// <summary>
	/// Restores the process-wide rendering preference.
	/// </summary>
	[TestCleanup]
	public void Cleanup()
	{
		PostProcessSystem.EnablePostProcess = previousPostProcessing;
	}

	/// <summary>
	/// Creates an infinite effect volume with the requested weight and priority.
	/// </summary>
	static PostProcessVolume CreateVolume( Scene scene, float value, float weight = 1, int priority = 0 )
	{
		var go = scene.CreateObject();
		var volume = go.Components.Create<PostProcessVolume>();
		volume.SceneVolume = new SceneVolume { Type = SceneVolume.VolumeTypes.Infinite };
		volume.BlendWeight = weight;
		volume.Priority = priority;
		go.Components.Create<CameraStateTestEffect>().Value = value;
		return volume;
	}

	/// <summary>
	/// Camera-state effects build on capture, while existing effects retain their scene-stage cadence.
	/// </summary>
	[TestMethod]
	public void CameraEffectsBuildOnDemandWithoutRebuildingOrdinaryEffects()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		var camera = scene.CreateObject().Components.Create<CameraComponent>();
		camera.BackgroundColor = Color.Red;
		var state = camera.GameObject.Components.Create<CameraStateTestEffect>();
		state.Value = 0.7f;
		var ordinary = camera.GameObject.Components.Create<OrdinaryPostProcessTestEffect>();

		scene.GameTick();
		Assert.AreEqual( 0, state.RenderCalls );
		Assert.AreEqual( 1, ordinary.RenderCalls );

		camera.InitializeRendering();
		Assert.AreEqual( 1, state.RenderCalls );
		Assert.AreEqual( 1, ordinary.RenderCalls );
		Assert.AreEqual( 0.7f, camera.SceneCamera.BackgroundColor.r, 0.0001f );
		Assert.AreEqual( 0.7f, camera.SceneCamera.Tonemap.ExposureCompensation, 0.0001f );

		state.Value = 0.4f;
		camera.InitializeRendering();
		Assert.AreEqual( 2, state.RenderCalls );
		Assert.AreEqual( 1, ordinary.RenderCalls );
		Assert.AreEqual( 0.4f, camera.SceneCamera.BackgroundColor.r, 0.0001f );

		state.Enabled = false;
		camera.InitializeRendering();
		Assert.AreEqual( 2, state.RenderCalls );
		Assert.AreEqual( 1, ordinary.RenderCalls );
		Assert.AreEqual( Color.Red, camera.SceneCamera.BackgroundColor, "Removing the last effect must restore the camera default instead of preserving its previous output" );
	}

	/// <summary>
	/// Selected-camera effects run once on the viewport after its defaults and copied main-camera post processing.
	/// </summary>
	[TestMethod]
	public void EditorPreviewAppliesSelectedCameraStateAfterDefaults()
	{
		var scene = Scene.CreateEditorScene();
		try
		{
			using var scope = scene.Push();
			var main = scene.CreateObject().Components.Create<CameraComponent>();
			main.AutoExposure.Enabled = true;
			main.AutoExposure.Compensation = 5;
			var mainEffect = main.GameObject.Components.Create<CameraStateTestEffect>();
			mainEffect.Value = 0.1f;
			var viewport = scene.CreateObject().Components.Create<CameraComponent>();
			viewport.IsSceneEditorCamera = true;
			viewport.BackgroundColor = Color.Red;
			var selected = scene.CreateObject().Components.Create<CameraComponent>();
			selected.IsMainCamera = false;
			var selectedEffect = selected.GameObject.Components.Create<CameraStateTestEffect>();
			selectedEffect.Value = 0.7f;
			selectedEffect.BeforeApply = ( source, target ) =>
			{
				Assert.AreSame( selected, source );
				Assert.AreSame( viewport.SceneCamera, target );
			};
			scene.UpdateMainCamera();
			scene.Editor = new BetterFogTests.FogEditorSession( scene );
			scene.Editor.Selection.Set( selected.GameObject );

			viewport.InitializeRendering();
			Assert.AreEqual( 1, selectedEffect.RenderCalls );
			Assert.AreEqual( 0, mainEffect.RenderCalls );
			Assert.AreEqual( 0.7f, viewport.SceneCamera.BackgroundColor.r, 0.0001f );
			Assert.AreEqual( 0.7f, viewport.SceneCamera.Tonemap.ExposureCompensation, 0.0001f, "Copying the main camera's defaults must precede the selected camera effect" );

			selectedEffect.Enabled = false;
			viewport.InitializeRendering();
			Assert.AreEqual( 1, selectedEffect.RenderCalls );
			Assert.AreEqual( 0, mainEffect.RenderCalls );
			Assert.AreEqual( Color.Red, viewport.SceneCamera.BackgroundColor, "Disabling the selected camera's final effect clears its previous viewport output" );
			Assert.AreEqual( 5.0f, viewport.SceneCamera.Tonemap.ExposureCompensation );
		}
		finally
		{
			scene.Destroy();
		}
	}

	/// <summary>
	/// The existing GetWeighted API sees globals, camera-local components and priority-ordered volumes.
	/// </summary>
	[TestMethod]
	public void WeightedValuesComposeGlobalsCameraAndVolumes()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		var first = scene.CreateObject().Components.Create<CameraComponent>();
		var second = scene.CreateObject().Components.Create<CameraComponent>();
		scene.CreateObject().Components.Create<CameraStateTestEffect>().Value = 0.1f;
		first.GameObject.Components.Create<CameraStateTestEffect>().Value = 0.2f;
		CreateVolume( scene, 0.6f, 0.5f, 10 );
		CreateVolume( scene, 0.3f, 0.5f, 0 );

		first.CopyPostProcessing( first.SceneCamera );
		second.CopyPostProcessing( second.SceneCamera );
		Assert.AreEqual( 0.425f, first.SceneCamera.BackgroundColor.r, 0.0001f );
		Assert.AreEqual( 0.4f, second.SceneCamera.BackgroundColor.r, 0.0001f );
	}

	/// <summary>
	/// Disabled and nested structural owners never promote their effects into global or parent layers.
	/// </summary>
	[TestMethod]
	public void DisabledAndNestedOwnersDoNotLeakEffects()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		var camera = scene.CreateObject().Components.Create<CameraComponent>();
		scene.CreateObject().Components.Create<CameraStateTestEffect>().Value = 0.2f;
		var outer = CreateVolume( scene, 0.4f );
		var inner = CreateVolume( scene, 0.8f, 0.5f, 10 );
		inner.GameObject.Parent = outer.GameObject;

		camera.CopyPostProcessing( camera.SceneCamera );
		Assert.AreEqual( 0.6f, camera.SceneCamera.BackgroundColor.r, 0.0001f );

		inner.Enabled = false;
		camera.CopyPostProcessing( camera.SceneCamera );
		Assert.AreEqual( 0.4f, camera.SceneCamera.BackgroundColor.r, 0.0001f );

		outer.Enabled = false;
		camera.CopyPostProcessing( camera.SceneCamera );
		Assert.AreEqual( 0.2f, camera.SceneCamera.BackgroundColor.r, 0.0001f );
	}

	/// <summary>
	/// Captures refresh current authored settings and spatial selection without a scene tick or active camera.
	/// </summary>
	[TestMethod]
	public void InactiveCaptureUsesCurrentPositionAndSettings()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		var camera = scene.CreateObject().Components.Create<CameraComponent>();
		camera.Enabled = false;
		var global = scene.CreateObject().Components.Create<CameraStateTestEffect>();
		global.Value = 0.2f;
		var volume = CreateVolume( scene, 0.8f );
		volume.SceneVolume = new SceneVolume();
		volume.BlendDistance = 0;

		camera.InitializeRendering();
		Assert.AreEqual( 0.8f, camera.SceneCamera.BackgroundColor.r, 0.0001f );

		camera.WorldPosition = new Vector3( 500, 0, 0 );
		global.Value = 0.3f;
		camera.InitializeRendering();
		Assert.AreEqual( 0.3f, camera.SceneCamera.BackgroundColor.r, 0.0001f );
	}

	/// <summary>
	/// Reentering a shared effect for another capture restores its source, target and weighted components afterward.
	/// </summary>
	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void NestedCaptureRestoresOuterBuildContext( bool throwFromInnerCapture )
	{
		var scene = new Scene();
		using var scope = scene.Push();
		var first = scene.CreateObject().Components.Create<CameraComponent>();
		var second = scene.CreateObject().Components.Create<CameraComponent>();
		second.WorldPosition = new Vector3( 500, 0, 0 );
		var global = scene.CreateObject().Components.Create<CameraStateTestEffect>();
		global.Value = 0.2f;
		var volume = CreateVolume( scene, 0.8f, 0.5f );
		volume.SceneVolume = new SceneVolume();
		volume.BlendDistance = 0;
		global.BeforeApply = ( source, target ) =>
		{
			if ( source == second )
			{
				Assert.AreSame( second.SceneCamera, target );

				if ( throwFromInnerCapture )
					throw new InvalidOperationException( "Nested capture failed" );
			}
			else
			{
				Assert.AreSame( first, source );
				Assert.AreSame( first.SceneCamera, target );

				if ( throwFromInnerCapture )
				{
					Assert.ThrowsException<InvalidOperationException>( () => second.CopyPostProcessing( second.SceneCamera ) );
				}
				else
				{
					second.CopyPostProcessing( second.SceneCamera );
				}
			}
		};

		first.CopyPostProcessing( first.SceneCamera );
		Assert.AreEqual( 0.5f, first.SceneCamera.BackgroundColor.r, 0.0001f );
		Assert.AreEqual( 2, global.RenderCalls );

		if ( !throwFromInnerCapture )
			Assert.AreEqual( 0.2f, second.SceneCamera.BackgroundColor.r, 0.0001f );
	}
}
