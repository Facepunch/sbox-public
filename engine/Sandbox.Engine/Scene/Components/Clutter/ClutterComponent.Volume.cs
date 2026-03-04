namespace Sandbox.Clutter;

/// <summary>
/// Volume/baked clutter mode - generates once within bounds
/// </summary>
public sealed partial class ClutterComponent
{
	[Property, Group( "Volume" ), ShowIf( nameof( Infinite ), false )]
	public BBox Bounds { get; set; } = new BBox( new Vector3( -512, -512, -512 ), new Vector3( 512, 512, 512 ) );

	/// <summary>
	/// Storage for volume model instances. Serialized with component.
	/// </summary>
	[Property, Hide]
	public ClutterGridSystem.ClutterStorage Storage { get; set; } = new();

	/// <summary>
	/// Layer used for rendering volume model instances.
	/// </summary>
	private ClutterLayer _volumeLayer;

	/// <summary>
	/// Tracks pending tile count for progressive volume generation.
	/// </summary>
	private int _pendingVolumeTiles;

	/// <summary>
	/// Total tiles queued for current generation.
	/// </summary>
	private int _totalVolumeTiles;

	private Editor.IProgressSection _progressSection;

	[Button( "Generate" )]
	[Icon( "scatter_plot" )]
	public void Generate()
	{
		if ( Infinite ) return;

		Clear();

		if ( Clutter == null )
			return;

		var gridSystem = Scene.GetSystem<ClutterGridSystem>();
		if ( gridSystem == null ) return;

		var settings = GetCurrentSettings();
		_volumeLayer ??= gridSystem.GetOrCreateLayer( this, settings );

		Storage ??= new();
		Storage.ClearAll();

		var worldBounds = Bounds.Transform( WorldTransform );
		var tileSize = Clutter.TileSize;

		// Calculate tile grid covering the volume
		var minTile = new Vector2Int(
			(int)Math.Floor( worldBounds.Mins.x / tileSize ),
			(int)Math.Floor( worldBounds.Mins.y / tileSize )
		);
		var maxTile = new Vector2Int(
			(int)Math.Floor( worldBounds.Maxs.x / tileSize ),
			(int)Math.Floor( worldBounds.Maxs.y / tileSize )
		);


		_pendingVolumeTiles = 0;
		_totalVolumeTiles = 0;

		// Queue generation jobs for each tile
		for ( int x = minTile.x; x <= maxTile.x; x++ )
			for ( int y = minTile.y; y <= maxTile.y; y++ )
			{
				var tileBounds = new BBox(
					new Vector3( x * tileSize, y * tileSize, worldBounds.Mins.z ),
					new Vector3( (x + 1) * tileSize, (y + 1) * tileSize, worldBounds.Maxs.z )
				);

				if ( !tileBounds.Overlaps( worldBounds ) )
					continue;

				var scatterBounds = new BBox(
						Vector3.Max( tileBounds.Mins, worldBounds.Mins ),
						Vector3.Min( tileBounds.Maxs, worldBounds.Maxs )
					);

				var job = new ClutterGenerationJob
				{
					Clutter = Clutter,
					Parent = GameObject,
					Bounds = scatterBounds,
					Seed = Scatterer.GenerateSeed( Seed, x, y ),
					Ownership = ClutterOwnership.Component,
					Layer = _volumeLayer,
					Storage = Storage,
					LocalBounds = Bounds,
					VolumeTransform = WorldTransform,
					OnComplete = () => _pendingVolumeTiles--
				};

				gridSystem.QueueJob( job );
				_pendingVolumeTiles++;
				_totalVolumeTiles++;
			}


		if ( _totalVolumeTiles > 8 && Scene.IsEditor )
		{
			_progressSection = Application.Editor.ProgressSection();
		}
	}

	internal void UpdateVolumeProgress()
	{
		// Only track progress if we have pending tiles and we're in editor
		if ( _totalVolumeTiles == 0 || !Scene.IsEditor )
		{
			if ( _progressSection != null )
			{
				_progressSection.Dispose();
				_progressSection = null;
			}
			return;
		}

		// Show progress for larger generations
		if ( _totalVolumeTiles > 8 )
		{
			var processed = _totalVolumeTiles - _pendingVolumeTiles;

			if ( _progressSection == null )
			{
				_progressSection = Application.Editor.ProgressSection();
			}

			if ( _progressSection.GetCancel().IsCancellationRequested )
			{
				CancelGeneration();
				return;
			}

			_progressSection.Title = "Generating Clutter";
			_progressSection.Subtitle = $"Processing tile {processed}/{_totalVolumeTiles}";
			_progressSection.TotalCount = _totalVolumeTiles;
			_progressSection.Current = processed;
			if ( _pendingVolumeTiles == 0 )
			{
				_totalVolumeTiles = 0;
				_progressSection?.Dispose();
				_progressSection = null;
			}
		}
	}

	/// <summary>
	/// Cancels ongoing volume generation.
	/// </summary>
	private void CancelGeneration()
	{
		var gridSystem = Scene.GetSystem<ClutterGridSystem>();
		if ( gridSystem != null )
		{
			gridSystem.ClearComponent( this );
		}

		// Reset tracking
		_pendingVolumeTiles = 0;
		_totalVolumeTiles = 0;
		if ( _progressSection != null )
		{
			_progressSection.Dispose();
			_progressSection = null;
		}

	}

	/// <summary>
	/// Rebuilds the visual layer from stored model instances.
	/// Called on scene load and when entering play mode.
	/// </summary>
	internal void RebuildVolumeLayer()
	{
		if ( Storage == null || Storage.TotalCount == 0 )
		{
			_volumeLayer?.ClearAllTiles();
			return;
		}

		var gridSystem = Scene.GetSystem<ClutterGridSystem>();
		if ( gridSystem == null ) return;

		var settings = GetCurrentSettings();
		_volumeLayer ??= gridSystem.GetOrCreateLayer( this, settings );
		_volumeLayer.ClearAllTiles();

		// Rebuild model instances from storage
		foreach ( var modelPath in Storage.ModelPaths )
		{
			var model = ResourceLibrary.Get<Model>( modelPath );
			if ( model == null ) continue;

			foreach ( var instance in Storage.GetInstances( modelPath ) )
			{
				_volumeLayer.AddModelInstance( Vector2Int.Zero, new ClutterInstance
				{
					Transform = new Transform( instance.Position, instance.Rotation, instance.Scale ),
					Entry = new ClutterEntry { Model = model }
				} );
			}
		}

		_volumeLayer.RebuildBatches();
	}

	[Button( "Clear" )]
	[Icon( "delete" )]
	public void Clear()
	{
		ClearInfinite();
		ClearVolume();
	}

	private void ClearVolume()
	{
		Storage?.ClearAll();
		_volumeLayer?.ClearAllTiles();

		// Ensure any in-progress volume generation UI/state is cleaned up
		_progressSection?.Dispose();
		_progressSection = null;
		_pendingVolumeTiles = 0;
		_totalVolumeTiles = 0;

		// Destroy prefab children
		var children = GameObject.Children.Where( c => c.Tags.Has( "clutter" ) ).ToArray();
		foreach ( var child in children )
			child.Destroy();
	}

	private void DrawVolumeGizmos()
	{
		if ( !Gizmo.IsSelected )
			return;

		using ( Gizmo.Scope( "volume" ) )
		{
			Gizmo.Draw.Color = Color.Green.WithAlpha( 0.3f );
			Gizmo.Draw.LineBBox( Bounds );

			Gizmo.Draw.Color = Color.Green.WithAlpha( 0.05f );

			if ( Gizmo.Control.BoundingBox( "bounds", Bounds, out var newBounds ) )
			{
				Bounds = newBounds;
			}
		}
	}
}
