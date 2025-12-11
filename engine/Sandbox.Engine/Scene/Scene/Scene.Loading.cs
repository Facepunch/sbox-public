namespace Sandbox;

public partial class Scene : GameObject
{
	List<Task> loadingTasks = new List<Task>();
	Task loadingMainTask;

	internal void AddLoadingTask( Task loadingTask )
	{
		loadingTasks.Add( loadingTask );
	}

	public void StartLoading()
	{
		if ( loadingMainTask is not null )
			return;

		loadingMainTask = WaitForLoading();
	}

	/// <summary>
	/// Return true if we're in an initial loading phase
	/// </summary>
	public bool IsLoading
	{
		get
		{
			loadingTasks.RemoveAll( x => x.IsCompleted );

			if ( loadingMainTask is null ) return false;
			if ( loadingMainTask.IsCompleted ) return false;

			return true;
		}
	}

	/// <summary>
	/// Wait for scene loading to finish
	/// </summary>
	internal async Task WaitForLoading()
	{
		if ( loadingMainTask is not null )
		{
			await loadingMainTask;
			return;
		}

		try
		{
			var instance = IGameInstance.Current;

			// wait one frame for all the tasks to build up
			await Task.Yield();

			// wait for all the loading tasks to finish
			while ( loadingTasks.Any() )
			{
				await Task.WhenAll( loadingTasks );
				loadingTasks.RemoveAll( x => x.IsCompleted );
			}

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

				await NavMesh.Generate( PhysicsWorld );

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
			loadingMainTask = default;
		}
	}
}
