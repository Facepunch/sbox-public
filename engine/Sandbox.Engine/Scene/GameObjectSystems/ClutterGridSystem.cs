namespace Sandbox.Clutter;

/// <summary>
/// Game object system that manages clutter generation.
/// Handles infinite streaming layers and executes generation jobs.
/// </summary>
public sealed partial class ClutterGridSystem : GameObjectSystem
{
	/// <summary>
	/// Mapping of clutter components to their respective layers
	/// </summary>
	private readonly Dictionary<ClutterComponent, ClutterLayer> _componentToLayer = [];

	private const int MAX_JOBS_PER_FRAME = 8;
	private const int MAX_PENDING_JOBS = 100;

	private readonly List<ClutterGenerationJob> _pendingJobs = [];
	private readonly HashSet<ClutterTile> _pendingTiles = [];
	private readonly HashSet<Terrain> _subscribedTerrains = [];
	private Vector3 _lastCameraPosition;

	/// <summary>
	/// Storage for painted clutter model instances.
	/// Serialized with the scene - this is the source of truth for painted clutter.
	/// </summary>
	[Property]
	public ClutterStorage Storage { get; set; } = new();

	/// <summary>
	/// Layer for rendering painted model instances from Storage.
	/// This is transient - rebuilt from Storage on scene load.
	/// </summary>
	private ClutterLayer _painted;

	private bool _dirty = false;

	public ClutterGridSystem( Scene scene ) : base( scene )
	{
		Listen( Stage.FinishUpdate, 0, OnUpdate, "ClutterGridSystem.Update" );
		Listen( Stage.SceneLoaded, 0, RebuildPaintedLayer, "ClutterGridSystem.RestorePainted" );
	}

	/// <summary>
	/// Check for new terrains, queue generation/cleanup jobs, and process pending jobs.
	/// </summary>
	private void OnUpdate()
	{
		var camera = GetActiveCamera();
		if ( camera == null )
			return;

		_lastCameraPosition = camera.WorldPosition;

		SubscribeToTerrains();
		UpdateInfiniteLayers( _lastCameraPosition );
		ProcessJobs();

		if ( _dirty )
		{
			RebuildPaintedLayer();
			_dirty = false;
		}
	}

	private void SubscribeToTerrains()
	{
		foreach ( var terrain in Scene.GetAllComponents<Terrain>() )
		{
			if ( _subscribedTerrains.Add( terrain ) )
			{
				terrain.OnTerrainModified += OnTerrainModified;
			}
		}

		_subscribedTerrains.RemoveWhere( t => !t.IsValid() );
	}

	private void UpdateActiveComponents( List<ClutterComponent> components, Vector3 cameraPosition )
	{
		foreach ( var component in components )
		{
			var settings = component.GetCurrentSettings();
			if ( !settings.IsValid )
				continue;

			var layer = GetOrCreateLayer( component, settings );
			layer.UpdateSettings( settings );

			foreach ( var job in layer.UpdateTiles( cameraPosition ) )
				QueueJob( job );
		}
	}

	private void RemoveInactiveComponents( List<ClutterComponent> activeInfiniteComponents )
	{
		var toRemove = _componentToLayer.Keys
			.Where( c => !c.IsValid() || (c.Infinite && !activeInfiniteComponents.Contains( c )) )
			.ToList();

		foreach ( var component in toRemove )
		{
			_componentToLayer[component].ClearAllTiles();
			_componentToLayer.Remove( component );
		}
	}

	private void UpdateInfiniteLayers( Vector3 cameraPosition )
	{
		var activeComponents = Scene.GetAllComponents<ClutterComponent>()
			.Where( c => c.Active && c.Infinite )
			.ToList();

		RemoveInactiveComponents( activeComponents );
		UpdateActiveComponents( activeComponents, cameraPosition );
	}

	/// <summary>
	/// Queues a generation job for processing.
	/// </summary>
	internal void QueueJob( ClutterGenerationJob job )
	{
		if ( !job.Parent.IsValid() )
			return;

		// Prevent duplicate jobs for the same tile
		if ( job.Tile is not null && !_pendingTiles.Add( job.Tile ) )
			return;

		_pendingJobs.Add( job );
	}

	/// <summary>
	/// Removes a tile from pending set (called when tile is destroyed).
	/// </summary>
	internal void RemovePendingTile( ClutterTile tile )
	{
		_pendingTiles.Remove( tile );
		_pendingJobs.RemoveAll( job => job.Tile == tile );
	}

	/// <summary>
	/// Clears all tiles for a specific component.
	/// </summary>
	public void ClearComponent( ClutterComponent component )
	{
		// Remove any pending jobs for this component (both tile and volume jobs)
		_pendingJobs.RemoveAll( job => job.Parent == component.GameObject );

		if ( _componentToLayer.Remove( component, out var layer ) )
		{
			layer.ClearAllTiles();
		}
	}

	/// <summary>
	/// Invalidates the tile at the given world position for a component, causing it to regenerate.
	/// </summary>
	public void InvalidateTileAt( ClutterComponent component, Vector3 worldPosition )
	{
		if ( _componentToLayer.TryGetValue( component, out var layer ) )
		{
			layer.InvalidateTile( worldPosition );
		}
	}

	/// <summary>
	/// Invalidates all tiles within the given bounds for a component, causing them to regenerate.
	/// </summary>
	public void InvalidateTilesInBounds( ClutterComponent component, BBox bounds )
	{
		if ( _componentToLayer.TryGetValue( component, out var layer ) )
		{
			layer.InvalidateTilesInBounds( bounds );
		}
	}

	/// <summary>
	/// Invalidates all tiles within the given bounds for ALL infinite clutter components.
	/// Useful for terrain painting where you want to refresh all clutter layers.
	/// </summary>
	public void InvalidateTilesInBounds( BBox bounds )
	{
		foreach ( var layer in _componentToLayer.Values )
		{
			layer.InvalidateTilesInBounds( bounds );
		}
	}

	private void OnTerrainModified( Terrain.SyncFlags flags, RectInt region )
	{
		var bounds = TerrainRegionToWorldBounds( _subscribedTerrains.First(), region );
		InvalidateTilesInBounds( bounds );
	}

	private static BBox TerrainRegionToWorldBounds( Terrain terrain, RectInt region )
	{
		var terrainTransform = terrain.WorldTransform;
		var storage = terrain.Storage;

		// Convert pixel coordinates to normalized (0-1) coordinates
		var minNorm = new Vector2(
			(float)region.Left / storage.Resolution,
			(float)region.Top / storage.Resolution
		);
		var maxNorm = new Vector2(
			(float)region.Right / storage.Resolution,
			(float)region.Bottom / storage.Resolution
		);

		var terrainSize = storage.TerrainSize;
		var minLocal = new Vector3( minNorm.x * terrainSize, minNorm.y * terrainSize, -1000f );
		var maxLocal = new Vector3( maxNorm.x * terrainSize, maxNorm.y * terrainSize, 1000f );

		var minWorld = terrainTransform.PointToWorld( minLocal );
		var maxWorld = terrainTransform.PointToWorld( maxLocal );

		return new BBox( minWorld, maxWorld );
	}

	private CameraComponent GetActiveCamera()
	{
		return Scene.IsEditor
			? Scene.Camera
			: Scene.Camera; // Figure out a way to grab editor camera
	}

	internal ClutterLayer GetOrCreateLayer( ClutterComponent component, ClutterSettings settings )
	{
		if ( _componentToLayer.TryGetValue( component, out var layer ) )
			return layer;

		layer = new ClutterLayer( settings, component.GameObject, this );
		_componentToLayer[component] = layer;
		return layer;
	}

	private int _lastSortedJobCount = 0;

	private void ProcessJobs()
	{
		if ( _pendingJobs.Count == 0 )
			return;

		// Track which layers had tiles populated
		HashSet<ClutterLayer> layersToRebuild = [];

		_pendingJobs.RemoveAll( job =>
			!job.Parent.IsValid() ||
			job.Tile?.IsPopulated == true
		);

		// Only sort when job count changes significantly (avoid sorting every frame)
		if ( Math.Abs( _pendingJobs.Count - _lastSortedJobCount ) > 50 || _lastSortedJobCount == 0 )
		{
			_pendingJobs.Sort( ( a, b ) =>
			{
				// Use tile bounds for infinite mode, job bounds for volume mode
				var distA = a.Tile != null
					? a.Tile.Bounds.Center.Distance( _lastCameraPosition )
					: a.Bounds.Center.Distance( _lastCameraPosition );
				var distB = b.Tile != null
					? b.Tile.Bounds.Center.Distance( _lastCameraPosition )
					: b.Bounds.Center.Distance( _lastCameraPosition );
				return distA.CompareTo( distB );
			} );
			_lastSortedJobCount = _pendingJobs.Count;
		}

		// Process nearest tiles first
		int processed = 0;
		while ( processed < MAX_JOBS_PER_FRAME && _pendingJobs.Count > 0 )
		{
			var job = _pendingJobs[0];
			_pendingJobs.RemoveAt( 0 );

			if ( job.Tile != null )
				_pendingTiles.Remove( job.Tile );

			// Execute if still valid and not populated
			if ( job.Parent.IsValid() && job.Tile?.IsPopulated != true )
			{
				job.Execute();
				processed++;

				if ( job.Layer != null )
					layersToRebuild.Add( job.Layer );
			}
		}

		// Rebuild batches for layers that had tiles populated
		foreach ( var layer in layersToRebuild )
		{
			layer.RebuildBatches();
		}

		var infiniteJobs = _pendingJobs.Where( j => j.Tile != null ).ToList();
		if ( infiniteJobs.Count > MAX_PENDING_JOBS )
		{
			var toRemove = infiniteJobs.Skip( MAX_PENDING_JOBS ).ToList();
			foreach ( var job in toRemove )
			{
				_pendingTiles.Remove( job.Tile );
				_pendingJobs.Remove( job );
			}
		}
	}

	/// <summary>
	/// Paint instance. Rebuilds on next frame update.
	/// Models are batched, Prefabs become GameObjects.
	/// </summary>
	public void Paint( ClutterEntry entry, Vector3 pos, Rotation rot, float scale = 1f )
	{
		if ( entry == null || !entry.HasAsset ) return;

		if ( entry.Prefab != null )
		{
			var go = entry.Prefab.Clone( pos, rot );
			go.WorldScale = scale;
			go.SetParent( Scene );
			go.Tags.Add( "clutter_painted" );
		}
		else if ( entry.Model != null )
		{
			Storage.AddInstance( entry.Model.ResourcePath, pos, rot, scale );
			_dirty = true;
		}
	}

	/// <summary>
	/// Erase instances. Rebuilds on next frame update.
	/// Erases both model batches and prefab GameObjects.
	/// </summary>
	public void Erase( Vector3 pos, float radius )
	{
		var radiusSquared = radius * radius;
		if ( Storage.Erase( pos, radius ) > 0 )
		{
			_dirty = true;
		}

		// Only erase painted prefabs, not streamed/volume clutter
		var paintedObjects = Scene.FindAllWithTag( "clutter_painted" )
			.Where( go => go.WorldPosition.DistanceSquared( pos ) <= radiusSquared )
			.ToList();

		foreach ( var go in paintedObjects )
		{
			go.Destroy();
		}
	}

	/// <summary>
	/// Clears all painted clutter (both model instances from storage and prefab GameObjects).
	/// Does not affect clutter owned by ClutterComponent volumes.
	/// </summary>
	public void ClearAllPainted()
	{
		Storage.ClearAll();

		var paintedObjects = Scene.FindAllWithTag( "clutter_painted" ).ToList();
		foreach ( var go in paintedObjects )
		{
			go.Destroy();
		}

		_painted?.ClearAllTiles();
		_dirty = false;
	}

	/// <summary>
	/// Flush painted changes and rebuild visual batches immediately.
	/// </summary>
	public void Flush()
	{
		RebuildPaintedLayer();
		_dirty = false;
	}

	/// <summary>
	/// Rebuild the painted clutter layer from stored instances in Storage.
	/// </summary>
	private void RebuildPaintedLayer()
	{
		if ( Storage is null )
		{
			Storage = new ClutterStorage();
		}

		if ( Storage.TotalCount == 0 )
		{
			_painted?.ClearAllTiles();
			return;
		}

		// Create or reuse painted layer
		if ( _painted == null )
		{
			var settings = new ClutterSettings( 0, new ClutterDefinition() );
			_painted = new ClutterLayer( settings, null, this );
		}

		_painted.ClearAllTiles();

		foreach ( var modelPath in Storage.ModelPaths )
		{
			var model = ResourceLibrary.Get<Model>( modelPath );
			if ( model == null ) continue;

			foreach ( var instance in Storage.GetInstances( modelPath ) )
			{
				_painted.AddModelInstance( Vector2Int.Zero, new()
				{
					Transform = new( instance.Position, instance.Rotation, instance.Scale ),
					Entry = new() { Model = model }
				} );
			}
		}

		_painted.RebuildBatches();
	}
}
