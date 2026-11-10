using Sandbox.Engine.Resources;
using Sandbox.Navigation.Generation;
using Sandbox.Utility;
using Sandbox.Volumes;
using Sandbox;

namespace Sandbox.Navigation;

internal class NavMeshTileCache : IDisposable
{
	private Dictionary<Vector2Int, NavMeshTile> tileCache = new();

	private List<NavMeshSpatialAuxiliaryData> allSpatialExtraData = new( 256 );

	private sealed record AreaRegistry( Dictionary<NavMeshAreaDefinition, int> Ids, NavMeshAreaDefinition[] Definitions );
	private AreaRegistry areaRegistry = new( new(), new NavMeshAreaDefinition[32] );

	public NavMeshTileCache()
	{
		UpdateAreaIds();
	}

	public int AreaDefinitionToId( NavMeshAreaDefinition areaDefinition )
	{
		var registry = System.Threading.Volatile.Read( ref areaRegistry );
		return areaDefinition != null && registry.Ids.TryGetValue( areaDefinition, out int id ) ? id : Constants.WALKABLE_AREA;
	}

	public NavMeshAreaDefinition AreaIdToDefinition( int id ) => System.Threading.Volatile.Read( ref areaRegistry ).Definitions[id];

	public bool HasTile( Vector2Int tilePosition )
	{
		return tileCache.ContainsKey( tilePosition );
	}

	internal bool Contains( NavMeshTile tile ) => tileCache.TryGetValue( tile.TilePosition, out var current ) && ReferenceEquals( current, tile );

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

			if ( tile.IsHeightfieldBuildInProgress || tile.IsNavmeshBuildInProgress ) continue;

			// Only queue if not already queued
			if ( tile.IsFullRebuildRequested && !queuedHeightfieldTiles.Contains( tile.TilePosition ) )
			{
				heightfieldBuildQueue.Enqueue( tile.TilePosition );
				queuedHeightfieldTiles.Add( tile.TilePosition );
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

			if ( tileCache.TryGetValue( tilePosition, out NavMeshTile tile ) && !tile.IsHeightfieldBuildInProgress && !tile.IsNavmeshBuildInProgress )
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

			if ( tileCache.TryGetValue( tilePosition, out NavMeshTile tile ) && !tile.IsHeightfieldBuildInProgress && !tile.IsNavmeshBuildInProgress )
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
		var ids = new Dictionary<NavMeshAreaDefinition, int>();
		var definitions = new NavMeshAreaDefinition[32];
		int nextId = 8;
		foreach ( var definition in ResourceLibrary.GetAll<NavMeshAreaDefinition>().OrderBy( a => a.Priority ) )
		{
			if ( nextId == 24 )
			{
				Log.Warning( "NavMeshAreaDefinition limit reached. Max 16 area definitions are currently supported." );
				break;
			}
			ids.Add( definition, nextId );
			definitions[nextId++] = definition;
		}
		// Published registries are never mutated; generation and agent checks only read them.
		System.Threading.Volatile.Write( ref areaRegistry, new AreaRegistry( ids, definitions ) );
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
			if ( !spatialExtraData.HasChanged && !spatialExtraData.IsPendingRemoval ) continue;
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
						if ( spatialExtraData is NavMeshAreaData && !spatialExtraData.IsBlocked ) navTile.RequestFullRebuild();
					}
				}
			}

			foreach ( var tile in currentTiles )
			{
				if ( spatialExtraData is NavMeshAreaData && !spatialExtraData.IsBlocked && !spatialExtraData.IsPendingRemoval )
					GetOrAddTile( tile );
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
							if ( spatialExtraData is NavMeshAreaData && !spatialExtraData.IsBlocked ) navTile.RequestFullRebuild();
						}
						else
						{
							if ( spatialExtraData is NavMeshAreaData && !spatialExtraData.IsBlocked )
								navTile.RequestFullRebuild();
							else
								navTile.RequestNavmeshBuild();
						}
					}
				}
			}

			if ( spatialExtraData.IsPendingRemoval )
			{
				allSpatialExtraData.RemoveAt( i );
			}
			spatialExtraData.HasChanged = false;
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
		tileCache.Clear();
		heightfieldBuildQueue.Clear();
		navmeshBuildQueue.Clear();
		queuedHeightfieldTiles.Clear();
		queuedNavmeshTiles.Clear();
		foreach ( var data in allSpatialExtraData ) data.HasChanged = true;
	}
}

internal abstract class NavMeshSpatialAuxiliaryData
{
	// Either scale, position, rotation or something else changed
	public bool HasChanged;

	public bool IsPendingRemoval = false;

	public bool IsBlocked = false;

	public NavMeshAreaDefinition AreaDefinition;

	public HashSet<Vector2Int> CurrentOverlappingTiles => currentOverlappingTiles;

	private HashSet<Vector2Int> currentOverlappingTiles = new();

	public HashSet<Vector2Int> PreviousOverlappingTiles => previousOverlappingTiles;

	private HashSet<Vector2Int> previousOverlappingTiles = new();

	protected abstract RectInt CalculateCurrentOverlappingTiles( NavMesh navMesh );

	internal void UpdateOverlappingTiles( NavMesh navMesh )
	{
		previousOverlappingTiles.Clear();
		foreach ( var tile in currentOverlappingTiles )
		{
			previousOverlappingTiles.Add( tile );
		}
		currentOverlappingTiles.Clear();

		var minMaxTileCoord = CalculateCurrentOverlappingTiles( navMesh );

		for ( int x = minMaxTileCoord.Left; x <= minMaxTileCoord.Right; x++ )
		{
			for ( int y = minMaxTileCoord.Top; y <= minMaxTileCoord.Bottom; y++ )
			{
				currentOverlappingTiles.Add( new Vector2Int( x, y ) );
			}
		}
	}
}

internal class NavMeshAreaData : NavMeshSpatialAuxiliaryData
{
	public BBox WorldBounds;

	public BBox LocalBounds;

	public Transform Transform;

	public SceneVolume Volume;

	protected override RectInt CalculateCurrentOverlappingTiles( NavMesh navMesh )
	{
		if ( Volume.Type == SceneVolume.VolumeTypes.Infinite ) return navMesh.CalculateMinMaxTileCoords( navMesh.Bounds );

		return navMesh.CalculateMinMaxTileCoords( WorldBounds );
	}
}

internal class NavMeshLinkData : NavMeshSpatialAuxiliaryData
{
	public Vector3 StartPosition;
	public Vector3 EndPosition;

	public bool IsBiDirectional = true;

	public float ConnectionRadius;

	// Used to associate this object with recast data
	public object UserData = null;

	public bool IsStartConnected = false;

	public bool IsEndConnected = false;

	public Vector3 StartPositionOnNavMesh;

	public Vector3 EndPositionOnNavMesh;

	protected override RectInt CalculateCurrentOverlappingTiles( NavMesh navMesh )
	{
		// Create a box that encompasses both start and end positions with radius
		Vector3 min = Vector3.Min( StartPosition, EndPosition ) - new Vector3( ConnectionRadius );
		Vector3 max = Vector3.Max( StartPosition, EndPosition ) + new Vector3( ConnectionRadius );
		BBox linkBounds = new BBox( min, max );

		// Get all tiles that this bounds overlaps
		return navMesh.CalculateMinMaxTileCoords( linkBounds );
	}
}
