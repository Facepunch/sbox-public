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
	/// Batches organized by (model, lod level), containing all instances across all tiles in this layer.
	/// </summary>
	private readonly Dictionary<(Model, int), ClutterBatchSceneObject> _batches = [];

	private int _lastSettingsHash;
	private const float TileHeight = 50000f;
	private const float LodRebuildThreshold = 50f;
	private bool _dirty = false;
	private Vector3 _lastLodCameraPos;

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
		foreach ( var coord in toRemove )
		{
			if ( Tiles.Remove( coord, out var tile ) )
			{
				GridSystem?.RemovePendingTile( tile );
				tile.Destroy();
				ModelInstancesByTile.Remove( coord );
			}
		}
		if ( toRemove.Count > 0 ) _dirty = true;

		var shouldRebuild = _dirty || Vector3.DistanceBetween( center, _lastLodCameraPos ) >= LodRebuildThreshold;
		if ( shouldRebuild && jobs.Count == 0 )
		{
			_lastLodCameraPos = center;
			RebuildBatches();
		}

		return jobs;
	}

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

	public void RebuildBatches()
	{
		var scene = ParentObject?.Scene ?? GridSystem?.Scene;
		if ( scene?.SceneWorld == null ) { _dirty = false; return; }

		var camera = scene.Camera;

		foreach ( var batch in _batches.Values )
			batch.Clear();

		var activeKeys = new HashSet<(Model, int)>();

		foreach ( var (tileCoord, instances) in ModelInstancesByTile )
		{
			var tileCenter = GetTileBounds( tileCoord ).Center.WithZ( camera?.WorldPosition.z ?? 0 );

			foreach ( var instance in instances )
			{
				if ( instance.Entry?.Model == null ) continue;

				var model = instance.Entry.Model;
				var lod = 0;
				if ( camera?.SceneCamera is not null )
				{
					var screenPixels = camera.SceneCamera.ComputeScreenSizeInPixels( tileCenter, 0.5f );
					var scale = instance.Transform.Scale;
					var instanceScale = MathF.Max( scale.x, MathF.Max( scale.y, scale.z ) );
					lod = model.GetLodLevelForScreenSize( screenPixels, instanceScale );
				}

				var key = (model, lod);
				activeKeys.Add( key );

				if ( !_batches.TryGetValue( key, out var batch ) )
				{
					batch = new ClutterBatchSceneObject( scene.SceneWorld, lod );
					_batches[key] = batch;
				}

				batch.AddInstance( instance );
			}
		}

		foreach ( var key in activeKeys )
			_batches[key].BuildCommandList();

		// Remove batches that are no longer needed
		foreach ( var key in _batches.Keys.Where( k => !activeKeys.Contains( k ) ).ToList() )
		{
			_batches[key].Delete();
			_batches.Remove( key );
		}

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

		foreach ( var batch in _batches.Values )
			batch.Delete();

		_batches.Clear();
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

	private Vector2Int WorldToTile( Vector3 worldPos ) => new(
		(int)MathF.Floor( worldPos.x / Settings.Clutter.TileSize ),
		(int)MathF.Floor( worldPos.y / Settings.Clutter.TileSize )
	);

	private BBox GetTileBounds( Vector2Int coord ) => new(
		new Vector3( coord.x * Settings.Clutter.TileSize, coord.y * Settings.Clutter.TileSize, -TileHeight ),
		new Vector3( (coord.x + 1) * Settings.Clutter.TileSize, (coord.y + 1) * Settings.Clutter.TileSize, TileHeight )
	);
}
