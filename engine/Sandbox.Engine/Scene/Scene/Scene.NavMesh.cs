namespace Sandbox;

public partial class Scene : GameObject
{
	public Navigation.NavMesh NavMesh { get; private set; } = new Navigation.NavMesh();

	private Task _navMeshLoadTask;

	/// <summary>
	/// In editor this gets called every frame
	/// In game this gets called every fixed update
	/// </summary>
	void Nav_Update()
	{
		if ( !NavMesh.IsEnabled || this is PrefabScene ) return;

		if ( !NavMesh.IsLoaded && IsEditor )
		{
			// Start loading if not already in progress
			if ( _navMeshLoadTask is null || _navMeshLoadTask.IsCompleted )
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

		if ( NavMesh.IsDirty || (NavMesh.DrawMesh && NavMesh.EditorAutoUpdate && IsEditor) )
		{
			NavMesh.InvalidateAllTiles( PhysicsWorld );
		}

		NavMesh.UpdateCache( PhysicsWorld );

		NavMesh.crowd.Update( Time.Delta, new DotRecast.Detour.Crowd.DtCrowdAgentDebugInfo() );
	}
}
