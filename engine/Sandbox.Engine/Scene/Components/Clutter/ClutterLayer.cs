namespace Sandbox.Clutter;

class ClutterLayer
{
	private Dictionary<Vector2Int, ClutterTile> Tiles { get; } = [];

	public ClutterSettings Settings { get; set; }

	/// <summary>
	/// Game object clutter will be placed under this parent
	/// </summary>
	public GameObject ParentObject { get; set; }

	public ClutterGridSystem GridSystem { get; set; }

	/// <summary>
	/// Model instances organized by tile coordinate.
	/// </summary>
	private Dictionary<Vector2Int, List<ClutterInstance>> ModelInstancesByTile { get; } = [];

	/// <summary>
	/// Batches organized by model. LOD is computed on the GPU per view, so batches are keyed by model.
	/// </summary>
	private readonly record struct ClutterBatchKey( Model Model, bool CastShadows );

	private readonly Dictionary<ClutterBatchKey, ClutterBatch> _batches = [];

	/// <summary>
	/// The scene's renderer, which owns and draws this layer's batches.
	/// </summary>
	private ClutterRenderer _renderer;

	private readonly Dictionary<Vector2Int, Dictionary<ClutterBatchKey, ClutterBatch.PreparedInstances>> _preparedTiles = [];
	private readonly Dictionary<ClutterBatchKey, List<ClutterBatch.PreparedInstances>> _instancesByModel = [];
	private readonly HashSet<Vector2Int> _renderDirtyTiles = [];
	private readonly HashSet<ClutterBatchKey> _changedModels = [];
	private readonly List<ClutterBatch.PreparedInstances> _retiredPrepared = [];

	private readonly HashSet<Vector2Int> _activeCoords = [];
	private readonly List<Vector2Int> _coordsToRemove = [];
	private readonly List<ClutterGenerationJob> _pendingJobs = [];

	/// <summary>
	/// Static collision bodies organized by tile coordinate. The layer owns collision
	/// alongside rendering, so every instance source (streamed, volume, painted) gets the
	/// same physics behaviour without duplicating body lifecycle logic.
	/// </summary>
	private readonly Dictionary<Vector2Int, List<PhysicsBody>> _bodiesByTile = [];

	private int _lastSettingsHash;
	private const float TileHeight = 50000f;
	private bool _dirty = false;

	public ClutterLayer( ClutterSettings settings, GameObject parentObject, ClutterGridSystem gridSystem )
	{
		Settings = settings;
		ParentObject = parentObject;
		GridSystem = gridSystem;
		_lastSettingsHash = settings.GetHashCode();
	}

	public void UpdateSettings( ClutterSettings newSettings )
	{
		var newHash = newSettings.GetHashCode();
		if ( newHash == _lastSettingsHash )
			return;

		// Mark all tiles as needing regeneration (keeps old content visible)
		foreach ( var tile in Tiles.Values )
		{
			tile.IsPopulated = false;
		}

		Settings = newSettings;
		_lastSettingsHash = newHash;
	}

	public List<ClutterGenerationJob> UpdateTiles( Vector3 center )
	{
		_pendingJobs.Clear();
		if ( !Settings.IsValid )
			return _pendingJobs;

		var centerTile = WorldToTile( center );
		_activeCoords.Clear();
		var jobs = _pendingJobs;

		for ( int x = -Settings.Clutter.TileRadius; x <= Settings.Clutter.TileRadius; x++ )
			for ( int y = -Settings.Clutter.TileRadius; y <= Settings.Clutter.TileRadius; y++ )
			{
				var coord = new Vector2Int( centerTile.x + x, centerTile.y + y );
				_activeCoords.Add( coord );

				// Get or create tile
				if ( !Tiles.TryGetValue( coord, out var tile ) )
				{
					tile = new ClutterTile
					{
						Coordinates = coord,
						Bounds = GetTileBounds( coord ),
						SeedOffset = Settings.RandomSeed
					};
					Tiles[coord] = tile;
				}

				// Queue job if not populated
				if ( !tile.IsPopulated )
				{
					jobs.Add( new ClutterGenerationJob
					{
						Clutter = Settings.Clutter,
						Parent = ParentObject,
						Bounds = tile.Bounds,
						Seed = Settings.RandomSeed,
						Ownership = ClutterOwnership.GridSystem,
						Layer = this,
						Tile = tile
					} );
				}
			}

		// Remove out-of-range tiles
		_coordsToRemove.Clear();
		foreach ( var coord in Tiles.Keys )
			if ( !_activeCoords.Contains( coord ) ) _coordsToRemove.Add( coord );

		foreach ( var coord in _coordsToRemove )
		{
			if ( Tiles.Remove( coord, out var tile ) )
			{
				GridSystem?.RemovePendingTile( tile );
				tile.Destroy();
				ClearTileModelInstances( coord );
			}
		}
		if ( _coordsToRemove.Count > 0 ) _dirty = true;

		if ( _dirty && jobs.Count == 0 )
			RebuildBatches();

		return jobs;
	}

	public void OnTilePopulated( ClutterTile tile )
	{
		_dirty = true;
	}

	/// <summary>
	/// Rebuilds batches if the instance set changed. LOD is GPU-side, so this ignores camera movement.
	/// </summary>
	public void RebuildIfDirty()
	{
		if ( _dirty )
			RebuildBatches();
	}

	/// <summary>
	/// Clears model instances and collision bodies for a specific tile coordinate.
	/// </summary>
	public void ClearTileModelInstances( Vector2Int tileCoord )
	{
		ModelInstancesByTile.Remove( tileCoord );
		_renderDirtyTiles.Add( tileCoord );
		RemoveBodies( tileCoord );
	}

	/// <summary>
	/// </summary>
	public void AddModelInstance( Vector2Int tileCoord, ClutterInstance instance )
	{
		if ( instance.Entry?.Model == null )
			return;

		if ( !ModelInstancesByTile.TryGetValue( tileCoord, out var instances ) )
		{
			instances = [];
			ModelInstancesByTile[tileCoord] = instances;
		}

		instances.Add( instance );
		_renderDirtyTiles.Add( tileCoord );

		TryCreateBody( tileCoord, instance );
	}

	public void ReserveModelInstances( Vector2Int tileCoord, int additionalCount )
	{
		if ( additionalCount == 0 )
			return;

		if ( !ModelInstancesByTile.TryGetValue( tileCoord, out var instances ) )
		{
			ModelInstancesByTile[tileCoord] = new List<ClutterInstance>( additionalCount );
			return;
		}

		instances.EnsureCapacity( instances.Count + additionalCount );
	}

	/// <summary>
	/// Takes ownership of a generated tile's model instances without copying its backing array.
	/// </summary>
	internal void AdoptModelInstances( Vector2Int tileCoord, List<ClutterInstance> instances )
	{
		ModelInstancesByTile[tileCoord] = instances;
		_renderDirtyTiles.Add( tileCoord );

		foreach ( var instance in instances )
			TryCreateBody( tileCoord, instance );
	}

	/// <summary>
	/// Populates this layer from a clutter storage, creating render batches and collision
	/// bodies for every stored instance. Shared by the painted and volume rebuild paths.
	/// </summary>
	public void PopulateFromStorage( ClutterGridSystem.ClutterStorage storage )
	{
		ClearAllTiles();

		if ( storage == null )
			return;

		foreach ( var modelPath in storage.ModelPaths )
		{
			var model = Model.Load( modelPath );
			if ( model == null ) continue;

			foreach ( var instance in storage.GetInstances( modelPath ) )
			{
				AddModelInstance( WorldToTile( instance.Position ), new ClutterInstance
				{
					Transform = new Transform( instance.Position, instance.Rotation, instance.Scale ),
					Entry = new ClutterEntry { Model = model }
				} );
			}
		}

		RebuildBatches();
	}

	/// <summary>
	/// Creates a static collision body for an instance (if its model has physics) and tracks it by tile.
	/// </summary>
	private void TryCreateBody( Vector2Int tileCoord, ClutterInstance instance )
	{
		var model = instance.Entry?.Model;
		if ( model?.Physics?.Parts.Count is not > 0 )
			return;

		if ( instance.Entry?.EnablePhysics is false )
			return;

		var scene = ParentObject?.Scene ?? GridSystem?.Scene;
		if ( scene == null )
			return;

		var body = ClutterGenerationJob.CreateStaticBodyForVolume( model, instance.Transform, scene );
		if ( body == null )
			return;

		if ( !_bodiesByTile.TryGetValue( tileCoord, out var bodies ) )
		{
			bodies = [];
			_bodiesByTile[tileCoord] = bodies;
		}

		bodies.Add( body );
	}

	/// <summary>
	/// Removes all collision bodies tracked for a tile coordinate.
	/// </summary>
	private void RemoveBodies( Vector2Int tileCoord )
	{
		if ( !_bodiesByTile.Remove( tileCoord, out var bodies ) )
			return;

		foreach ( var body in bodies )
			if ( body.IsValid() ) body.Remove();
	}

	public void RebuildBatches()
	{
		// Don't build batch list on headless. We only care about collisions.
		if ( !Graphics.IsAvailable )
		{
			_dirty = false;
			return;
		}

		_renderer ??= GridSystem?.Renderer;
		if ( _renderer == null )
		{
			_dirty = false;
			return;
		}

		_changedModels.Clear();
		_retiredPrepared.Clear();

		foreach ( var tileCoord in _renderDirtyTiles )
		{
			if ( _preparedTiles.Remove( tileCoord, out var previous ) )
			{
				foreach ( var (key, tile) in previous )
				{
					_instancesByModel[key].Remove( tile );
					_changedModels.Add( key );
					_retiredPrepared.Add( tile );
				}
			}

			if ( !ModelInstancesByTile.TryGetValue( tileCoord, out var instances ) || instances.Count == 0 )
				continue;

			var countsByModel = new Dictionary<ClutterBatchKey, int>();
			foreach ( var instance in instances )
			{
				if ( instance.Entry?.Model == null )
					continue;

				var key = new ClutterBatchKey( instance.Entry.Model, instance.Entry.CastShadows );
				countsByModel.TryGetValue( key, out var count );
				countsByModel[key] = count + 1;
			}

			var prepared = new Dictionary<ClutterBatchKey, ClutterBatch.PreparedInstances>( countsByModel.Count );
			foreach ( var (key, count) in countsByModel )
				prepared[key] = new ClutterBatch.PreparedInstances( key.Model, count );

			foreach ( var instance in instances )
			{
				if ( instance.Entry?.Model == null )
					continue;

				var key = new ClutterBatchKey( instance.Entry.Model, instance.Entry.CastShadows );
				prepared[key].Add( instance.Transform );
			}

			_preparedTiles[tileCoord] = prepared;

			foreach ( var (key, tile) in prepared )
			{
				_changedModels.Add( key );
				if ( !_instancesByModel.TryGetValue( key, out var list ) )
				{
					list = [];
					_instancesByModel[key] = list;
				}
				list.Add( tile );
			}
		}

		_renderDirtyTiles.Clear();

		foreach ( var key in _changedModels )
		{
			var tiles = _instancesByModel[key];
			if ( tiles.Count == 0 )
			{
				if ( _batches.Remove( key, out var emptyBatch ) )
					_renderer.RemoveBatch( emptyBatch );

				_instancesByModel.Remove( key );
				continue;
			}

			if ( !_batches.TryGetValue( key, out var batch ) )
			{
				batch = _renderer.CreateBatch( key.Model, key.CastShadows );
				_batches[key] = batch;
			}

			_renderer.SetInstances( batch, tiles );
		}

		// Batches must release the old tile references before their buffers can be reused.
		foreach ( var tile in _retiredPrepared )
			tile.Dispose();

		_retiredPrepared.Clear();

		_dirty = false;
	}

	public void ClearAllTiles()
	{
		foreach ( var tile in Tiles.Values )
		{
			GridSystem?.RemovePendingTile( tile );
			tile.Destroy();
		}

		Tiles.Clear();
		ModelInstancesByTile.Clear();

		foreach ( var prepared in _preparedTiles.Values )
		{
			foreach ( var tile in prepared.Values )
				tile.Dispose();
		}

		_preparedTiles.Clear();
		_renderDirtyTiles.Clear();
		_changedModels.Clear();
		_retiredPrepared.Clear();

		// Copied out first, RemoveBodies mutates the dictionary.
		_coordsToRemove.Clear();
		foreach ( var coord in _bodiesByTile.Keys )
			_coordsToRemove.Add( coord );

		foreach ( var coord in _coordsToRemove )
			RemoveBodies( coord );

		foreach ( var batch in _batches.Values )
			_renderer?.RemoveBatch( batch );

		_batches.Clear();
		_instancesByModel.Clear();
		_dirty = false;
	}

	/// <summary>
	/// Invalidates the tile at the given world position, causing it to regenerate.
	/// </summary>
	public void InvalidateTile( Vector3 worldPosition )
	{
		var coord = WorldToTile( worldPosition );
		if ( Tiles.TryGetValue( coord, out var tile ) )
		{
			GridSystem?.RemovePendingTile( tile );
			tile.Destroy();
			ClearTileModelInstances( coord );
			_dirty = true;
		}
	}

	/// <summary>
	/// Invalidates all tiles that intersect the given bounds, causing them to regenerate.
	/// </summary>
	public void InvalidateTilesInBounds( BBox bounds )
	{
		var minTile = WorldToTile( bounds.Mins );
		var maxTile = WorldToTile( bounds.Maxs );

		for ( int x = minTile.x; x <= maxTile.x; x++ )
			for ( int y = minTile.y; y <= maxTile.y; y++ )
			{
				var coord = new Vector2Int( x, y );
				if ( Tiles.TryGetValue( coord, out var tile ) )
				{
					GridSystem?.RemovePendingTile( tile );
					tile.Destroy();
					ClearTileModelInstances( coord );
					_dirty = true;
				}
			}
	}

	private Vector2Int WorldToTile( Vector3 worldPos ) => new(
		(int)MathF.Floor( worldPos.x / Settings.Clutter.TileSize ),
		(int)MathF.Floor( worldPos.y / Settings.Clutter.TileSize )
	);

	private BBox GetTileBounds( Vector2Int coord ) => new(
		new Vector3( coord.x * Settings.Clutter.TileSize, coord.y * Settings.Clutter.TileSize, -TileHeight ),
		new Vector3( (coord.x + 1) * Settings.Clutter.TileSize, (coord.y + 1) * Settings.Clutter.TileSize, TileHeight )
	);
}
