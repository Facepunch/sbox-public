using Sandbox.Volumes;
namespace Sandbox;

/// <summary>
/// Manages post-processing effects for cameras and volumes within a scene, handling their application during rendering
/// and editor preview stages.
/// </summary>
/// <remarks>This system coordinates the collection and application of post-process effects based on camera
/// position and active post-process volumes. In editor mode, it supports previewing effects for selected volumes or
/// cameras. Implements both scene stage and render thread interfaces to integrate with the rendering
/// pipeline.</remarks>
public sealed partial class PostProcessSystem : GameObjectSystem<PostProcessSystem>, Component.ISceneStage, Component.IRenderThread
{
	[ConVar( "r_postprocess", ConVarFlags.Saved, Help = "Enable or disable post process effects." )]
	internal static bool EnablePostProcess { get; set; } = true;

	public PostProcessSystem( Scene scene ) : base( scene )
	{

	}

	/// <summary>
	/// Called at the very end of the scene update, after all other components have been ticked.
	/// We use it to update our post processing effects for each camera.
	/// </summary>
	void Component.ISceneStage.End()
	{
		if ( !EnablePostProcess )
			return;

		if ( Application.IsDedicatedServer )
			return;

		//
		// Editor behavior is special
		//
		if ( Scene.IsEditor )
		{
			UpdateEditorScene();
			return;
		}

		foreach ( var cc in Scene.GetAll<CameraComponent>() )
		{
			UpdateCamera( cc );
		}
	}

	void UpdateEditorScene()
	{
		if ( Scene.Camera is null )
			return;

		Scene.Camera.PostProcess.Clear();

		Scene.Camera.AutoExposure.Enabled = true;
		Scene.Camera.AutoExposure.Compensation = 0;
		Scene.Camera.AutoExposure.Rate = 20;
		Scene.Camera.AutoExposure.MinimumExposure = 1;
		Scene.Camera.AutoExposure.MaximumExposure = 2;

		//
		// If we have an object selected
		//
		if ( Scene.Editor?.SelectedGameObject is GameObject go )
		{
			//
			// And it's a camera
			//
			if ( go.GetComponentInParent<CameraComponent>( false, true ) is CameraComponent cc )
			{
				UpdateCamera( cc );
				return;
			}

			//
			// Or if it's a volume
			//
			if ( go.GetComponentInParent<PostProcessVolume>( false, true ) is PostProcessVolume volume && volume.EditorPreview )
			{
				PreviewVolume( volume );
				return;
			}
		}

		//
		// By default just update the main camera
		//
		if ( Scene.Camera is CameraComponent mainCamera )
		{
			UpdateCamera( mainCamera );
		}
	}



	private void UpdateCamera( CameraComponent cc )
	{
		cc.PostProcess.Clear();

		if ( !cc.EnablePostProcessing )
			return;

		var pos = cc.PostProcessAnchor.IsValid() ? cc.PostProcessAnchor.WorldPosition : cc.WorldPosition;

		List<WeightedEffect> effects = cc.GetComponentsInChildren<BasePostProcess>()
										.Select( x => new WeightedEffect { Effect = x, Weight = 1 } )
										.ToList();

		var volumes = Scene.GetSystem<VolumeSystem>()?.FindAll<PostProcessVolume>( pos );
		foreach ( var volume in volumes.OrderBy( x => x.Priority ) )
		{
			var weight = volume.GetWeight( pos );
			effects.AddRange( volume.GetComponentsInChildren<BasePostProcess>().Select( x => new WeightedEffect { Effect = x, Weight = weight } ) );
		}

		foreach ( var group in effects.GroupBy( x => x.Effect.GetType() ) )
		{
			var effect = group.First();

			var ctx = new PostProcessContext()
			{
				Camera = cc,
				Components = group.ToArray()
			};

			effect.Effect.Build( ctx );
		}
	}

	/// <summary>
	/// Called in editor mode, when a volume is selected
	/// </summary>
	private void PreviewVolume( PostProcessVolume volume )
	{
		var data = Scene.Camera.PostProcess;
		var pos = volume.WorldPosition;

		List<WeightedEffect> effects = volume.GetComponentsInChildren<BasePostProcess>().Select( x => new WeightedEffect { Effect = x, Weight = 1 } ).ToList();

		foreach ( var group in effects.GroupBy( x => x.Effect.GetType() ) )
		{
			var effect = group.First();

			var ctx = new PostProcessContext()
			{
				Camera = Scene.Camera,
				Components = group.ToArray()
			};

			effect.Effect.Build( ctx );
		}
	}

	/// <summary>
	/// Called whenever a camera is rendering a specific stage. This is called on the render thread.
	/// </summary>
	void Component.IRenderThread.OnRenderStage( CameraComponent camera, Sandbox.Rendering.Stage stage )
	{
		if ( !EnablePostProcess )
			return;

		if ( Graphics.SceneView.GetPostProcessEnabled() == false )
			return;

		// Don't run explicit post process effects if we're in ToolsVis, other command lists like SSR/SSAO should still run
		if ( Graphics.SceneView.GetToolsVisMode() != (int)SceneCameraDebugMode.Normal &&
				stage >= Rendering.Stage.BeforePostProcess &&
				stage <= Rendering.Stage.AfterPostProcess )
			return;

		camera.PostProcess.OnRenderStage( stage );
	}
}
