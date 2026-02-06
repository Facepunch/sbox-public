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

	public void StartLoading()
	{
		if ( _loadingMainTask is not null )
			return;

		_loadingMainTask = WaitForLoading();
	}

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
	internal async Task WaitForLoading()
	{
		if ( _loadingMainTask is not null )
		{
			await _loadingMainTask;
			return;
		}

		try
		{
			var instance = IGameInstance.Current;

			// wait one frame for all the tasks to build up
			await Task.Yield();

			// wait for all the loading tasks to finish
			while ( _loadingTasks.Count > 0 )
			{
				LoadingScreen.UpdateLoadingTasks( _loadingTasks );
				await Task.WhenAny( _loadingTasks.Select( x => x.Task ) );
				_loadingTasks.RemoveAll( x => x.IsCompleted );
			}

			// Remove all the tasks
			LoadingScreen.UpdateLoadingTasks( [] );

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

				LoadingScreen.Subtitle = "Loading Finished..";
			}

			if ( !IsValid ) return;

			using ( Push() )
			{
				// tell the game instance we finished loading
				instance?.OnLoadingFinished();

				// shoot events
				RunEvent<ISceneLoadingEvents>( x => x.AfterLoad( this ) );

				// Run pending startups
				RunPendingStarts();

				// Tell networking we've finished loading, lets players join
				var sceneInformation = Components.Get<SceneInformation>();
				SceneNetworkSystem.OnLoadedScene( sceneInformation?.Title );
			}
		}
		finally
		{
			_loadingMainTask = default;
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
