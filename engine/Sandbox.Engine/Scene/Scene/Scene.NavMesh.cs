namespace Sandbox;

public partial class Scene : GameObject
{
	public Navigation.NavMesh NavMesh { get; private set; } = new Navigation.NavMesh();

	private Task _navMeshLoadTask;

	/// <summary>
	/// Set by a scene load that skipped the loading flow (ShowLoadingScreen = false), which is what loads the navmesh in game.
	/// </summary>
	private bool _navMeshLoadPending;

	/// <summary>
	/// In editor this gets called every frame
	/// In game this gets called every fixed update
	/// </summary>
	void Nav_Update()
	{
		if ( !NavMesh.IsEnabled || this is PrefabScene ) return;

		if ( NavMesh.IsLoaded ) _navMeshLoadPending = false;

		if ( !NavMesh.IsLoaded && (IsEditor || _navMeshLoadPending) )
		{
			// Start loading if not already in progress
			if ( !NavMesh.IsGenerating && (_navMeshLoadTask is null || _navMeshLoadTask.IsCompleted) )
			{
				_navMeshLoadTask = NavMesh.Load( PhysicsWorld );
				_navMeshLoadTask.ContinueWith( t =>
				{
					_navMeshLoadTask = null;

					if ( t.Exception != null )
					{
						Log.Warning( $"NavMesh load failed: {t.Exception.InnerException?.Message ?? t.Exception.Message}" );
					}
				} );
			}
			return;
		}

		if ( NavMesh.IsGenerating ) return;

		if ( IsEditor ) BakeMissingInEditor();

		if ( NavMesh.IsDirty || (NavMesh.AutoUpdates && IsEditor) )
		{
			NavMesh.InvalidateAllTiles( PhysicsWorld );
		}

		NavMesh.UpdateCache( PhysicsWorld );

		NavMesh.Simulation.Update( Time.Delta );
	}

	/// <summary>
	/// An editor scene that loaded without its bake has just generated, and bakes that.
	/// </summary>
	void BakeMissingInEditor()
	{
		if ( NavMesh.TakeMissingBake() && Editor is { HasUnsavedChanges: false } editor && editor.GetSceneFolder() is { } folder )
		{
			_ = NavMesh.BakeInEditor( PhysicsWorld, folder, () => !editor.HasUnsavedChanges, regenerate: false );
		}
	}
}
