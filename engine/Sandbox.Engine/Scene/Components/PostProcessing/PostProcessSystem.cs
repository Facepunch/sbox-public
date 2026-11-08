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

		foreach ( var cc in Scene.Query<CameraComponent>() )
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

		var pos = cc.PostProcessPosition;

		var scratch = RentBuildScratch();
		try
		{
			scratch.AddEffects( cc.GameObject, 1 );

			scratch.CollectVolumes( Scene, pos );
			foreach ( var entry in scratch.Volumes )
				scratch.AddEffects( entry.Volume.GameObject, entry.Volume.GetWeight( pos ) );

			scratch.Build( cc );
		}
		finally
		{
			ReturnBuildScratch( scratch );
		}
	}

	/// <summary>
	/// Called in editor mode, when a volume is selected
	/// </summary>
	private void PreviewVolume( PostProcessVolume volume )
	{
		var scratch = RentBuildScratch();
		try
		{
			scratch.AddEffects( volume.GameObject, 1 );
			scratch.Build( Scene.Camera );
		}
		finally
		{
			ReturnBuildScratch( scratch );
		}
	}

	/// <summary>
	/// Build camera effects directly into the destination view, including inactive captures and editor viewports.
	/// Uses the same effect grouping, volume ordering and volume weights as command-list effects.
	/// </summary>
	internal void ApplyCameraEffects( CameraComponent camera, SceneCamera target, bool preview = false )
	{
		ThreadSafe.AssertIsMainThread();
		if ( !EnablePostProcess || !camera.EnablePostProcessing || Application.IsDedicatedServer )
			return;

		PostProcessVolume previewVolume = null;
		if ( preview && Scene.IsEditor && Scene.Editor is { IsActive: true, SelectedGameObject: { } selected } )
		{
			var owner = GetOwner( selected );
			if ( owner is CameraComponent selectedCamera )
			{
				camera = selectedCamera;
			}
			else if ( owner is PostProcessVolume { EditorPreview: true } volume )
			{
				previewVolume = volume;
			}
		}

		if ( !camera.EnablePostProcessing )
			return;

		var scratch = RentBuildScratch();
		try
		{
			if ( !scratch.CollectCameraEffects( Scene ) )
				return;

			scratch.AddCameraEffects( null, 1 );
			if ( previewVolume is not null )
			{
				if ( previewVolume.Active )
					scratch.AddCameraEffects( previewVolume, 1 );
			}
			else
			{
				scratch.AddCameraEffects( camera, 1 );
				var position = camera.PostProcessPosition;
				scratch.CollectVolumes( Scene, position );
				foreach ( var entry in scratch.Volumes )
				{
					var volume = entry.Volume;
					if ( Scene.IsEditor && !volume.EditorPreview )
						continue;

					var weight = volume.GetWeight( position );
					scratch.AddCameraEffects( volume, weight.Clamp( 0, 1 ) );
				}
			}

			scratch.Build( camera, target );
		}
		finally
		{
			ReturnBuildScratch( scratch );
		}
	}

	/// <summary>
	/// Nearest structural owner, including disabled components. A colocated volume takes precedence over a camera.
	/// </summary>
	static Component GetOwner( GameObject gameObject )
	{
		for ( var go = gameObject; go.IsValid(); go = go.Parent )
		{
			if ( go.GetComponent<PostProcessVolume>( true ) is { } volume )
				return volume;

			if ( go.GetComponent<CameraComponent>( true ) is { } camera )
				return camera;
		}

		return null;
	}

	/// <summary>
	/// Called whenever a camera is rendering a specific stage. This is called on the render thread.
	/// </summary>
	void Component.IRenderThread.OnRenderStage( CameraComponent camera, Sandbox.Rendering.Stage stage )
	{
		if ( !EnablePostProcess )
			return;

		if ( Graphics.PostProcessEnabled == false )
			return;

		// Don't run explicit post process effects if we're in ToolsVis, other command lists like SSR/SSAO should still run
		if ( Graphics.ToolsVisMode != (int)SceneCameraDebugMode.Normal &&
				stage >= Rendering.Stage.BeforePostProcess &&
				stage <= Rendering.Stage.AfterPostProcess )
			return;

		camera.PostProcess.OnRenderStage( stage );
	}
}
