using Sandbox.Engine.Resources;
using Sandbox.Navigation.Generation;
using Sandbox.Utility;

namespace Sandbox.Navigation;

internal class NavMeshTileCache : IDisposable
{
	private Dictionary<Vector2Int, NavMeshTile> tileCache = new();

	private List<NavMeshSpatialAuxiliaryData> allSpatialExtraData = new( 256 );

	private Dictionary<NavMeshAreaDefinition, int> areaDefinitionToId = new( 64 );
	private List<NavMeshAreaDefinition> areaIdToDefinition = new( 64 );
	private int areaDefinitionIdCounter = 0;

	public NavMeshTileCache()
	{
		UpdateAreaIds();
	}

	public int AreaDefinitionToId( NavMeshAreaDefinition areaDefinition )
	{
		if ( areaDefinition == null ) return Constants.WALKABLE_AREA;
		if ( areaDefinitionToId.TryGetValue( areaDefinition, out int id ) ) return id;

		return Constants.WALKABLE_AREA;
	}

	public NavMeshAreaDefinition AreaIdToDefinition( int id )
	{
		return areaIdToDefinition[id];
	}

	public void RemoveTile( Vector2Int tilePosition )
	{
		tileCache.Remove( tilePosition );
	}

	private UniqueQueue<Vector2Int> heightfieldBuildQueue = new();
	private UniqueQueue<Vector2Int> navmeshBuildQueue = new();

	private HashSet<Vector2Int> queuedHeightfieldTiles = new();
	private HashSet<Vector2Int> queuedNavmeshTiles = new();

	public NavMeshTile GetOrAddTile( Vector2Int tilePosition )
	{
		if ( !tileCache.TryGetValue( tilePosition, out NavMeshTile tile ) )
		{
			tile = new NavMeshTile();
			tile.TilePosition = tilePosition;
			foreach ( var spatialData in allSpatialExtraData )
			{
				var currentTiles = spatialData.CurrentOverlappingTiles;
				if ( currentTiles.Contains( tilePosition ) )
				{
					tile.AddSpatialData( spatialData );
				}
			}
			tileCache.Add( tilePosition, tile );
		}

		return tile;
	}

	public void Update( NavMesh navMesh, PhysicsWorld physicsWorld )
	{
		UpdateAreas( navMesh );

		var heightfieldBuildsThisUpdate = 0;
		var heightfieldBuildsInProgress = 0;

		var navmeshBuildsInProgress = 0;
		var navmeshBuildsThisUpdate = 0;

		foreach ( var tile in tileCache.Values )
		{
			if ( tile.IsHeightfieldBuildInProgress ) heightfieldBuildsInProgress++;
			if ( tile.IsNavmeshBuildInProgress ) navmeshBuildsInProgress++;

			// Only queue if not already queued
			if ( tile.IsFullRebuildRequested && !tile.IsNavmeshBuildInProgress && !queuedHeightfieldTiles.Contains( tile.TilePosition ) )
			{
				heightfieldBuildQueue.Enqueue( tile.TilePosition );
				queuedHeightfieldTiles.Add( tile.TilePosition );
				continue;
			}

			// We can't build a navmesh if the heightfield is still being built
			if ( tile.IsHeightfieldBuildInProgress )
			{
				continue;
			}

			if ( tile.IsHeightFieldValid && tile.IsNavmeshBuildRequested && !queuedNavmeshTiles.Contains( tile.TilePosition ) )
			{
				navmeshBuildQueue.Enqueue( tile.TilePosition );
				queuedNavmeshTiles.Add( tile.TilePosition );
			}
		}

		// Process heightfield builds
		var allowedHeightFieldBuildsThisUpdate = Math.Max( 2, (int)(NavMesh.HeightFieldGenerationThreadCount / 1.5) ) - heightfieldBuildsInProgress;

		while ( heightfieldBuildsThisUpdate < allowedHeightFieldBuildsThisUpdate && heightfieldBuildQueue.Count > 0 )
		{
			var tilePosition = heightfieldBuildQueue.Dequeue();
			queuedHeightfieldTiles.Remove( tilePosition );

			if ( tileCache.TryGetValue( tilePosition, out NavMeshTile tile ) )
			{
				var success = tile.DispatchHeightFieldBuild( navMesh, physicsWorld );
				if ( success ) heightfieldBuildsThisUpdate++;
			}
		}

		// Process navmesh builds
		var allowedNavmeshBuildsThisUpdate = Math.Max( 2, NavMesh.NavMeshGenerationThreadCount ) - navmeshBuildsInProgress;

		while ( navmeshBuildsThisUpdate < allowedNavmeshBuildsThisUpdate && navmeshBuildQueue.Count > 0 )
		{
			var tilePosition = navmeshBuildQueue.Dequeue();
			queuedNavmeshTiles.Remove( tilePosition );

			if ( tileCache.TryGetValue( tilePosition, out NavMeshTile tile ) )
			{
				if ( tile.IsHeightFieldValid )
				{
					tile.DispatchNavmeshBuild( navMesh );
					navmeshBuildsThisUpdate++;
				}
			}
		}
	}

	public void UpdateAreaIds()
	{
		var allAreaDefinitions = ResourceLibrary.GetAll<NavMeshAreaDefinition>();
		areaDefinitionToId.Clear();
		areaIdToDefinition.Clear();
		for ( int i = 0; i < 32; i++ )
		{
			areaIdToDefinition.Add( null );
		}
		// Reserve a few ids for now
		areaDefinitionIdCounter = 8;

		var sortedAreaDefinitions = allAreaDefinitions.OrderBy( a => a.Priority );
		foreach ( var areaDefinition in sortedAreaDefinitions )
		{
			if ( areaDefinitionIdCounter == 24 )
			{
				Log.Warning( "NavMeshAreaDefinition limit reached. Max 24 area definitions are currently supported." );
			}
			else
			{
				areaDefinitionToId[areaDefinition] = areaDefinitionIdCounter;
				areaIdToDefinition[areaDefinitionIdCounter] = areaDefinition;
				areaDefinitionIdCounter++;
			}
		}
		InvalidateAllAreaIds();
	}

	private void InvalidateAllAreaIds()
	{
		foreach ( var spatialData in allSpatialExtraData )
		{
			if ( spatialData.AreaDefinition != null )
			{
				spatialData.HasChanged = true;
			}
		}
	}

	private void UpdateAreas( NavMesh navMesh )
	{
		// Iterating in reverse because we may remove stuff
		for ( int i = allSpatialExtraData.Count() - 1; i >= 0; i-- )
		{
			var spatialExtraData = allSpatialExtraData.ElementAt( i );
			spatialExtraData.UpdateOverlappingTiles( navMesh );

			var previousTiles = spatialExtraData.PreviousOverlappingTiles;
			var currentTiles = spatialExtraData.CurrentOverlappingTiles;

			foreach ( var tile in previousTiles )
			{
				if ( !currentTiles.Contains( tile ) )
				{
					if ( tileCache.TryGetValue( tile, out NavMeshTile navTile ) )
					{
						navTile.RemoveSpatialData( spatialExtraData );
					}
				}
			}

			foreach ( var tile in currentTiles )
			{
				if ( !previousTiles.Contains( tile ) )
				{
					if ( tileCache.TryGetValue( tile, out NavMeshTile navTile ) )
					{
						navTile.AddSpatialData( spatialExtraData );
					}
				}
			}

			if ( spatialExtraData.HasChanged || spatialExtraData.IsPendingRemoval )
			{
				foreach ( var tile in currentTiles )
				{
					if ( tileCache.TryGetValue( tile, out NavMeshTile navTile ) )
					{
						if ( spatialExtraData.IsPendingRemoval )
						{
							navTile.RemoveSpatialData( spatialExtraData );
						}
						else
						{
							navTile.RequestNavmeshBuild();
							spatialExtraData.HasChanged = false;
						}
					}
				}
			}

			if ( spatialExtraData.IsPendingRemoval )
			{
				allSpatialExtraData.RemoveAt( i );
			}
		}
	}

	internal void AddSpatiaData( NavMeshSpatialAuxiliaryData data )
	{
		allSpatialExtraData.Add( data );
	}

	/// <summary>
	/// Returns true if any tile has an in-progress heightfield or navmesh build.
	/// Used to wait for a stable state before baking.
	/// </summary>
	public bool HasAnyBuildsInProgress()
	{
		foreach ( var tile in tileCache.Values )
		{
			if ( tile.IsHeightfieldBuildInProgress || tile.IsNavmeshBuildInProgress )
				return true;
		}
		return false;
	}

	public void Dispose()
	{
		foreach ( var (_, tile) in tileCache )
		{
			tile.Dispose();
		}
	}
}

public sealed partial class NavMesh
{

	private NavMeshTileCache tileCache = new();

	internal void AddSpatiaData( NavMeshSpatialAuxiliaryData data )
	{
		tileCache.AddSpatiaData( data );
	}

	internal void UpdateCache( PhysicsWorld physicsWorld )
	{
		tileCache.Update( this, physicsWorld );
	}

	internal int AreaDefinitionToId( NavMeshAreaDefinition areaDefinition )
	{
		return tileCache.AreaDefinitionToId( areaDefinition );
	}

	internal NavMeshAreaDefinition AreaIdToDefinition( int id )
	{
		return tileCache.AreaIdToDefinition( id );
	}

	internal void UpdateAreaIds()
	{
		tileCache.UpdateAreaIds();
	}
}
