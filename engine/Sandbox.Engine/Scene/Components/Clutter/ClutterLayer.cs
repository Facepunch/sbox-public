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
	/// Batches organized by model, containing all instances across all tiles in this layer.
	/// </summary>
	private readonly Dictionary<Model, ClutterBatchSceneObject> _batches = [];

	private int _lastSettingsHash;
	private const float TileHeight = 50000f;

	/// <summary>
	/// batches need to be rebuilt
	/// </summary>
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
		if ( !Settings.IsValid )
			return [];

		var centerTile = WorldToTile( center );
		var activeCoords = new HashSet<Vector2Int>();
		var jobs = new List<ClutterGenerationJob>();

		for ( int x = -Settings.Clutter.TileRadius; x <= Settings.Clutter.TileRadius; x++ )
			for ( int y = -Settings.Clutter.TileRadius; y <= Settings.Clutter.TileRadius; y++ )
			{
				var coord = new Vector2Int( centerTile.x + x, centerTile.y + y );
				activeCoords.Add( coord );

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
		var toRemove = Tiles.Keys.Where( coord => !activeCoords.Contains( coord ) ).ToList();
		if ( toRemove.Count > 0 )
		{
			foreach ( var coord in toRemove )
			{
				if ( Tiles.Remove( coord, out var tile ) )
				{
					// Remove from pending set first to prevent queue buildup
					GridSystem?.RemovePendingTile( tile );
					tile.Destroy();

					// Remove model instances for this tile
					ModelInstancesByTile.Remove( coord );
				}
			}
			_dirty = true;
		}

		// Rebuild batches if needed
		if ( _dirty && jobs.Count == 0 )
		{
			RebuildBatches();
		}

		return jobs;
	}

	/// <summary>
	/// Called when a tile has been populated with instances.
	/// Marks batches as dirty so they'll be rebuilt.
	/// </summary>
	public void OnTilePopulated( ClutterTile tile )
	{
		_dirty = true;
	}

	/// <summary>
	/// Clears model instances for a specific tile coordinate.
	/// </summary>
	public void ClearTileModelInstances( Vector2Int tileCoord )
	{
		ModelInstancesByTile.Remove( tileCoord );
	}

	/// <summary>
	/// Adds a model instance for a specific tile.
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
	}

	/// <summary>
	/// Rebuilds all batches from scratch using all populated tiles.
	/// </summary>
	public void RebuildBatches()
	{
		var sceneWorld = ParentObject?.Scene?.SceneWorld ?? GridSystem?.Scene?.SceneWorld;
		if ( sceneWorld == null )
		{
			_dirty = false;
			return;
		}

		// Group instances by model
		var instancesByModel = ModelInstancesByTile.Values
			.SelectMany( instances => instances )
			.Where( i => i.Entry?.Model != null )
			.GroupBy( i => i.Entry.Model )
			.ToDictionary( g => g.Key, g => g.ToList() );

		foreach ( var batch in _batches.Values )
			batch.Clear();

		foreach ( var (model, instances) in instancesByModel )
		{
			if ( !_batches.TryGetValue( model, out var batch ) )
			{
				batch = new ClutterBatchSceneObject( sceneWorld );
				_batches[model] = batch;
			}

			foreach ( var instance in instances )
				batch.AddInstance( instance );
		}

		var toRemove = _batches.Keys.Where( m => !instancesByModel.ContainsKey( m ) ).ToList();
		foreach ( var model in toRemove )
		{
			_batches[model].Delete();
			_batches.Remove( model );
		}

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
			ModelInstancesByTile.Remove( coord );
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
					ModelInstancesByTile.Remove( coord );
					_dirty = true;
				}
			}
	}

	public void ClearAllTiles()
	{
		// Remove any pending tiles from the grid system
		foreach ( var tile in Tiles.Values )
		{
			GridSystem?.RemovePendingTile( tile );
			tile.Destroy();
		}

		Tiles.Clear();
		ModelInstancesByTile.Clear();

		// Clear all batches
		foreach ( var batch in _batches.Values )
		{
			batch.Delete();
		}
		_batches.Clear();
		_dirty = false;
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
