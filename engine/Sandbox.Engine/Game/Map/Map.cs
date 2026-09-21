using System.Threading;

namespace Sandbox;

public sealed class Map
{
	private PhysicsWorld PhysicsWorld;

	/// <summary>
	/// The world physics objects
	/// </summary>
	public PhysicsGroup PhysicsGroup { get; private set; }


	/// <summary>
	/// The world geometry;
	/// </summary>
	public SceneMap SceneMap { get; private set; }


	private Map()
	{
	}

	public Map( string mapName, MapLoader loader )
	{
		SceneMap = new SceneMap( loader.World, mapName, loader );
		PhysicsWorld = loader.PhysicsWorld;
		CreatePhysics( SceneMap.MapFolder, loader.WorldOrigin );
	}

	public static async Task<Map> CreateAsync( string mapName, MapLoader loader, CancellationToken cancelToken = default )
	{
		var sceneMap = await SceneMap.CreateAsync( loader.World, mapName, loader, cancelToken );
		if ( !sceneMap.IsValid() )
			return null;

		var map = new Map
		{
			SceneMap = sceneMap,
			PhysicsWorld = loader.PhysicsWorld
		};

		map.CreatePhysics( sceneMap.MapFolder, loader.WorldOrigin );

		return map;
	}

	private void CreatePhysics( string mapFolder, Vector3 origin )
	{
		if ( !PhysicsWorld.IsValid() )
			return;

		if ( PhysicsWorld?._world is not PhysicsWorld3d world3d )
			return;

		var physicsGroup = world3d.native.CreateAggregateInstance( $"{mapFolder}/world_physics.vphys", new Transform( origin ), 0, PhysicsMotionType.Static );
		if ( !physicsGroup.IsValid() )
		{
			Log.Warning( $"Couldn't find map physics: '{mapFolder}/world_physics.vphys'" );
			return;
		}

		PhysicsGroup = physicsGroup;
	}

	public void Delete()
	{
		if ( SceneMap.IsValid() )
		{
			SceneMap.Delete();
			SceneMap = null;
		}

		if ( PhysicsGroup.IsValid() && PhysicsWorld?._world is PhysicsWorld3d world3d )
		{
			world3d.native.DestroyAggregateInstance( PhysicsGroup );
			PhysicsGroup = null;
		}

		PhysicsWorld = null;
	}
}
