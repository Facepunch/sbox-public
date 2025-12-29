namespace Sandbox;

public partial class Scene : GameObject
{
	public Navigation.NavMesh NavMesh { get; private set; } = new Navigation.NavMesh();

	/// <summary>
	/// In editor this gets called every frame
	/// In game this gets called every fixed update
	/// </summary>
	void Nav_Update()
	{
		if ( !NavMesh.IsEnabled || this is PrefabScene )
			return;

		if ( NavMesh.IsGenerating ) return;

		if ( NavMesh.IsDirty || (NavMesh.DrawMesh && NavMesh.EditorAutoUpdate && IsEditor) )
		{
			NavMesh.InvalidateAllTiles( PhysicsWorld );
		}

		NavMesh.UpdateCache( PhysicsWorld );

		NavMesh.crowd.Update( Time.Delta, new DotRecast.Detour.Crowd.DtCrowdAgentDebugInfo() );
	}
}
