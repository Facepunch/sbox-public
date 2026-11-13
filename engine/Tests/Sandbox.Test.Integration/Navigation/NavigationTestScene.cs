using Sandbox.Navigation;
using Sandbox.Volumes;
using System;

namespace NavigationTests;

/// <summary>
/// What the navigation tests set up over and over: floors, agents, areas and links, and what the
/// scene does with its navmesh between the fixed updates of a game.
/// </summary>
internal static class NavigationTestScene
{
	/// <summary>
	/// A box collider of this size around the centre.
	/// </summary>
	public static GameObject Box( this Scene scene, Vector3 center, Vector3 size )
	{
		var go = scene.CreateObject();
		go.WorldPosition = center;
		go.Components.Create<BoxCollider>().Scale = size;
		return go;
	}

	/// <summary>
	/// A slab of this size on the ground plane whose top is at the centre's height.
	/// </summary>
	public static GameObject Floor( this Scene scene, Vector3 center, float size = 400 )
	{
		return scene.Box( center - Vector3.Up * 16, new Vector3( size, size, 32 ) );
	}

	public static void Remove( this Scene scene, GameObject go )
	{
		go.Destroy();
		scene.ProcessDeletes();
	}

	public static NavMeshAgent Agent( this Scene scene, Vector3 position = default )
	{
		var go = scene.CreateObject();
		go.WorldPosition = position;
		return go.Components.Create<NavMeshAgent>();
	}

	/// <summary>
	/// An enabled area with this shape, a blocker unless told otherwise.
	/// </summary>
	public static NavMeshArea Area( this Scene scene, Vector3 position, SceneVolume volume, bool blocker = true )
	{
		var go = scene.CreateObject();
		go.WorldPosition = position;
		var area = go.Components.Create<NavMeshArea>( false );
		area.IsBlocker = blocker;
		area.SceneVolume = volume;
		area.Enabled = true;
		return area;
	}

	public static NavMeshArea Area( this Scene scene, Vector3 position, Vector3 size, bool blocker = true )
	{
		return scene.Area( position, new SceneVolume { Type = SceneVolume.VolumeTypes.Box, Box = BBox.FromPositionAndSize( 0, size ) }, blocker );
	}

	/// <summary>
	/// An enabled link on a game object at the origin.
	/// </summary>
	public static T Link<T>( this Scene scene, Vector3 start, Vector3 end ) where T : NavMeshLink, new()
	{
		var link = scene.CreateObject().Components.Create<T>( false );
		link.LocalStartPosition = start;
		link.LocalEndPosition = end;
		link.Enabled = true;
		return link;
	}

	/// <summary>
	/// Scene.Nav_Update: every fixed update of a game and every frame of an editor scene.
	/// </summary>
	public static void NavUpdate( this Scene scene, float delta = 0.02f )
	{
		var navMesh = scene.NavMesh;
		if ( !navMesh.IsEnabled || navMesh.IsGenerating ) return;
		if ( navMesh.IsDirty ) navMesh.InvalidateAllTiles( scene.PhysicsWorld );
		navMesh.UpdateCache( scene.PhysicsWorld );
		navMesh.Simulation.Update( delta );
	}

	/// <summary>
	/// Navigation updates until no tile is dirty or being built.
	/// </summary>
	public static async Task Settle( this Scene scene )
	{
		for ( int i = 0; i < 5000; i++ )
		{
			scene.NavUpdate();
			if ( scene.NavMesh.mesh.PendingTiles == 0 ) return;
			await Task.Delay( 1 );
		}
		Assert.Fail( "The navmesh did not settle" );
	}

	/// <summary>
	/// True when the navmesh has a point within this distance of the point, on the ground plane, and near its height.
	/// </summary>
	public static bool OnMesh( this NavMesh navMesh, Vector3 point, float distance = 8 )
	{
		return navMesh.GetClosestPoint( point, MathF.Max( distance, 24 ) ) is Vector3 found && found.WithZ( 0 ).Distance( point.WithZ( 0 ) ) <= distance;
	}

	public static bool Allows( this Nav.NavFilter filter, int area ) => (filter.Forbidden >> area & 1) == 0;
}
