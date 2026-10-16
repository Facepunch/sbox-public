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

		var pos = cc.PostProcessAnchor.IsValid() ? cc.PostProcessAnchor.WorldPosition : cc.WorldPosition;

		var scratch = RentBuildScratch();
		try
		{
			scratch.AddEffects( cc.GameObject, 1 );

			foreach ( var volume in Scene.Query<PostProcessVolume>() )
			{
				if ( ((VolumeSystem.IVolume)volume).Test( pos ) )
					scratch.Volumes.Add( (volume, volume.Priority, scratch.Volumes.Count) );
			}

			// Match OrderBy's stable ordering when volumes have equal priority.
			scratch.Volumes.Sort( static ( a, b ) =>
			{
				var order = a.Priority.CompareTo( b.Priority );
				return order != 0 ? order : a.Index.CompareTo( b.Index );
			} );
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
