using NativeEngine;

namespace Sandbox;

public partial class Scene : GameObject
{
	readonly List<LoadingContext> _loadingTasks = [];
	Task _loadingMainTask;

	internal void AddLoadingTask( LoadingContext loadingTask )
	{
		_loadingTasks.Add( loadingTask );
		LoadingScreen.UpdateLoadingTasks( _loadingTasks );
	}

	/// <summary>
	/// Begin loading this scene and running its startup callbacks.
	/// </summary>
	public void StartLoading() => StartLoading( null );

	internal Task StartLoading( Func<bool> isCurrent ) => _loadingMainTask ??= WaitForLoading( isCurrent );

	/// <summary>
	/// Return true if we're in an initial loading phase
	/// </summary>
	public bool IsLoading
	{
		get
		{
			_loadingTasks.RemoveAll( x => x.IsCompleted );

			if ( _loadingMainTask is null ) return false;
			if ( _loadingMainTask.IsCompleted ) return false;

			return true;
		}
	}

	/// <summary>
	/// Wait for scene loading to finish
	/// </summary>
	internal async Task WaitForLoading( Func<bool> isCurrent = null )
	{
		bool IsStale() => isCurrent?.Invoke() == false;

		if ( _loadingMainTask is not null )
		{
			await _loadingMainTask;
			return;
		}

		var reveal = false;
		try
		{
			var instance = IGameInstance.Current;

			// wait one frame for all the tasks to build up
			await Task.Yield();

			if ( IsStale() )
				return;

			// wait for all the loading tasks to finish
			while ( _loadingTasks.Count > 0 )
			{
				LoadingScreen.UpdateLoadingTasks( _loadingTasks );
				await Task.WhenAny( _loadingTasks.Select( x => x.Task ) );

				if ( IsStale() )
					return;

				_loadingTasks.RemoveAll( x => x.IsCompleted );
			}

			// Remove all the tasks
			LoadingScreen.UpdateLoadingTasks( [] );

			if ( !IsValid ) return;

			// The models and textures the renderer still loads in the background for what's loaded
			if ( this is not PrefabScene ) await BorealisGpuRenderBackend.WaitForContent( this );

			if ( !IsValid ) return;

			//
			// Some people are locking up forever. Need more info.
			//

			//while ( NativeEngine.ResourceSystem.HasPendingWork() )
			//{
			//	LoadingScreen.Subtitle = "Loading Resources..";
			//	await Task.DelayRealtime( 100 );
			//}

			// generated after everything is loaded
			if ( NavMesh.IsEnabled && this is not PrefabScene )
			{
				LoadingScreen.Subtitle = "Generating NavMesh..";

				await NavMesh.Load( PhysicsWorld );

				if ( IsStale() )
					return;

				LoadingScreen.Subtitle = "Loading Finished..";
			}

			// While the loading screen is still up, give back the free space the load left in the managed heap
			if ( this is not PrefabScene ) await ClosedGameMemory.CompactAfterLoad( this );

			if ( !IsValid ) return;

			// Finish where the game ticks next, so its first tick and render are in the frame the loading screen goes in
			if ( this is not PrefabScene ) await BorealisGpuRenderBackend.BeforeLoadFinishes();

			if ( !IsValid ) return;

			using ( Push() )
			{
				// tell the game instance we finished loading
				instance?.OnLoadingFinished();

				if ( IsStale() )
					return;

				// shoot events
				RunEvent<ISceneLoadingEvents>( x => x.AfterLoad( this ) );

				if ( IsStale() )
					return;

				// Run pending startups
				RunPendingStarts();

				if ( IsStale() )
					return;

				// Pipelines the load made go to disk: the drawing device's
				if ( WantsSystemScene && this is not PrefabScene && !Application.IsUnitTest && RenderBackend.Default is BorealisGpuRenderBackend borealis )
					borealis.FlushPipelineCache();

				// Tell networking we've finished loading, lets players join
				var sceneInformation = Components.Get<SceneInformation>();
				SceneNetworkSystem.OnLoadedScene( sceneInformation?.Title );
			}

			// On Borealis.Gpu the screen stays up while the game's first frames draw behind it, the spawned player's view among them
			reveal = this is not PrefabScene && WantsSystemScene && BorealisGpuRenderBackend.HoldsReveal;
		}
		finally
		{
			_loadingMainTask = default;

			if ( !IsStale() )
			{
				if ( reveal ) _ = BorealisGpuRenderBackend.RevealWhenDrawn( this );
				else LoadingScreen.IsVisible = false;
			}
		}
	}
}

public class LoadingContext
{
	/// <summary>
	/// The title of this loading task
	/// </summary>
	public string Title { get; set; }

	/// <summary>
	/// True if the task has completed
	/// </summary>
	public bool IsCompleted => Task?.IsCompleted ?? true;

	/// <summary>
	/// The task itself
	/// </summary>
	internal Task Task { get; set; }
}
