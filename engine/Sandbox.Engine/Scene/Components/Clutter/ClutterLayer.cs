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
	/// Batches organized by model. The engine picks LODs per instance and view, so batches are keyed by model.
	/// </summary>
	private readonly record struct ClutterBatchKey( Model Model, bool CastShadows );

	/// <summary>
	/// One <see cref="SceneModelInstances"/> per model across every tile: the engine multi-draws one object's instances
	/// together, so an object per tile would make a draw per tile.
	/// </summary>
	private readonly Dictionary<ClutterBatchKey, SceneModelInstances> _batches = [];

	private readonly Dictionary<Vector2Int, Dictionary<ClutterBatchKey, PreparedInstances>> _preparedTiles = [];
	private readonly Dictionary<ClutterBatchKey, List<PreparedInstances>> _instancesByModel = [];
	private readonly HashSet<Vector2Int> _renderDirtyTiles = [];
	private readonly HashSet<ClutterBatchKey> _changedModels = [];
	private readonly List<PreparedInstances> _retiredPrepared = [];

	// A model's instances from every tile, gathered for SetInstances, which copies them.
	private Transform[] _gathered = [];

	/// <summary>
	/// One tile's instances of one model. The pooled buffer returns once the tile leaves its batch.
	/// </summary>
	private sealed class PreparedInstances : IDisposable
	{
		public Transform[] Transforms { get; }
		public int Count { get; }

		private int _next;

		public PreparedInstances( int count )
		{
			Count = count;
			Transforms = System.Buffers.ArrayPool<Transform>.Shared.Rent( count );
		}

		public void Add( Transform transform ) => Transforms[_next++] = transform;

		public void Dispose() => System.Buffers.ArrayPool<Transform>.Shared.Return( Transforms );
	}

	private readonly HashSet<Vector2Int> _activeCoords = [];
	private readonly List<Vector2Int> _coordsToRemove = [];
	private readonly List<ClutterGenerationJob> _pendingJobs = [];

	/// <summary>
	/// Static collision bodies organized by tile coordinate. The layer owns collision
	/// alongside rendering, so every instance source (streamed, volume, painted) gets the
	/// same physics behaviour without duplicating body lifecycle logic.
	/// </summary>
	private readonly Dictionary<Vector2Int, List<PhysicsBody>> _bodiesByTile = [];

	// Tiles within _collisionRadius tiles of _collisionCenter have bodies. -1 until the first UpdateCollision, so loading creates none.
	private Vector2Int _collisionCenter;
	private long _collisionRadius = -1;

	// Radius used when nothing culls collision: every tile is in range, and +1 for hysteresis can't overflow.
	private const long UnlimitedCollisionRadius = long.MaxValue - 1;

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

		if ( _bodiesByTile.TryGetValue( tileCoord, out var bodies ) )
			TryCreateBody( bodies, instance );
		else if ( InCollisionRange( tileCoord, _collisionRadius ) )
			ActivateCollision( tileCoord );
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

		// Rebuilt from the adopted list, so stale bodies from a previous population can't linger.
		RemoveBodies( tileCoord );
		if ( InCollisionRange( tileCoord, _collisionRadius ) )
			ActivateCollision( tileCoord );
	}

	/// <summary>
	/// Populates this layer from a clutter storage, creating render batches and collision
	/// bodies (for tiles in collision range) for every stored instance. Shared by the painted and volume rebuild paths.
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

			// One entry per model and physics flag: instances only read it, so a copy each is wasted memory.
			var withPhysics = new ClutterEntry { Model = model };
			var withoutPhysics = new ClutterEntry { Model = model, EnablePhysics = false };
			foreach ( var instance in storage.GetInstances( modelPath ) )
			{
				AddModelInstance( WorldToTile( instance.Position ), new ClutterInstance
				{
					Transform = new Transform( instance.Position, instance.Rotation, instance.Scale ),
					Entry = instance.EnablePhysics ? withPhysics : withoutPhysics
				} );
			}
		}

		RebuildBatches();
	}

	/// <summary>
	/// Keeps collision bodies only for tiles near <paramref name="center"/>, the way rendering culls instances out of view.
	/// A null center (no camera, e.g. a dedicated server) or a radius of 0 keeps collision on every tile.
	/// </summary>
	public void UpdateCollision( Vector3? center, float radius )
	{
		var cull = center.HasValue && radius > 0f;
		var centerTile = cull ? WorldToTile( center.Value ) : default;
		var tileRadius = cull ? (long)MathF.Ceiling( radius / Settings.Clutter.TileSize ) : UnlimitedCollisionRadius;

		if ( centerTile == _collisionCenter && tileRadius == _collisionRadius )
			return;

		_collisionCenter = centerTile;
		_collisionRadius = tileRadius;

		// Drop one tile later than we add, so moving along a tile edge doesn't rebuild bodies every frame.
		_coordsToRemove.Clear();
		foreach ( var coord in _bodiesByTile.Keys )
			if ( !InCollisionRange( coord, tileRadius + 1 ) ) _coordsToRemove.Add( coord );

		foreach ( var coord in _coordsToRemove )
			RemoveBodies( coord );

		foreach ( var coord in ModelInstancesByTile.Keys )
			if ( !_bodiesByTile.ContainsKey( coord ) && InCollisionRange( coord, tileRadius ) )
				ActivateCollision( coord );
	}

	private bool InCollisionRange( Vector2Int tileCoord, long tileRadius ) =>
		Math.Abs( (long)tileCoord.x - _collisionCenter.x ) <= tileRadius &&
		Math.Abs( (long)tileCoord.y - _collisionCenter.y ) <= tileRadius;

	/// <summary>
	/// Creates bodies for every instance in a tile and marks it as having collision, even if none of them have physics.
	/// </summary>
	private void ActivateCollision( Vector2Int tileCoord )
	{
		var bodies = new List<PhysicsBody>();
		_bodiesByTile[tileCoord] = bodies;

		if ( !ModelInstancesByTile.TryGetValue( tileCoord, out var instances ) )
			return;

		foreach ( var instance in instances )
			TryCreateBody( bodies, instance );
	}

	/// <summary>
	/// Creates a static collision body for an instance (if its model has physics) and adds it to the tile's bodies.
	/// </summary>
	private void TryCreateBody( List<PhysicsBody> bodies, ClutterInstance instance )
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

		var scene = ParentObject?.Scene ?? GridSystem?.Scene;
		if ( scene?.SceneWorld == null )
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

			var prepared = new Dictionary<ClutterBatchKey, PreparedInstances>( countsByModel.Count );
			foreach ( var (key, count) in countsByModel )
				prepared[key] = new PreparedInstances( count );

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
					emptyBatch.Delete();

				_instancesByModel.Remove( key );
				continue;
			}

			if ( !_batches.TryGetValue( key, out var batch ) )
			{
				batch = new SceneModelInstances( scene.SceneWorld, key.Model );
				batch.Flags.CastShadows = key.CastShadows;
				_batches[key] = batch;
			}

			int total = 0;
			foreach ( var tile in tiles )
				total += tile.Count;

			if ( _gathered.Length < total )
				_gathered = new Transform[Math.Max( total, _gathered.Length * 2 )];

			int at = 0;
			foreach ( var tile in tiles )
			{
				tile.Transforms.AsSpan( 0, tile.Count ).CopyTo( _gathered.AsSpan( at ) );
				at += tile.Count;
			}

			batch.SetInstances( _gathered.AsSpan( 0, total ) );
		}

		// The batches copied the transforms, so the buffers can be reused.
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
			batch.Delete();

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
