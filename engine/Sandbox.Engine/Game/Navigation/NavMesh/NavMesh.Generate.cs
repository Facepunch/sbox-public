using DotRecast.Detour;
using Sandbox.Navigation.Generation;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Sandbox.Navigation;

public sealed partial class NavMesh
{
	internal static int HeightFieldGenerationThreadCount
	{
		get
		{
			return Math.Max( 2, Environment.ProcessorCount - 1 );
		}
	}

	internal static int NavMeshGenerationThreadCount
	{
		get => HeightFieldGenerationThreadCount;
	}

	internal class GeneratorPool<T> : IDisposable where T : class, IDisposable, new()
	{
		public GeneratorPool( int poolSize )
		{
			for ( int i = 0; i < poolSize; i++ )
			{
				var generator = new T();
				_pool.Enqueue( generator );
			}
		}

		public T Get()
		{
			if ( _pool.TryDequeue( out var o ) )
				return o;

			return new T();
		}

		public void Return( T obj )
		{
			_pool.Enqueue( obj );
		}

		ConcurrentQueue<T> _pool = new();

		public void Dispose()
		{
			while ( _pool.TryDequeue( out var o ) )
			{
				o.Dispose();
			}
		}
	}


	static internal GeneratorPool<HeightFieldGenerator> HeightFieldGeneratorPool = new( HeightFieldGenerationThreadCount );
	static internal GeneratorPool<NavMeshGenerator> NavMeshGeneratorPool = new( NavMeshGenerationThreadCount );

	/// <summary>
	/// Generates or regenerates the navmesh tile at the given world position.
	/// This function is thread safe but can only be called from the main thread.
	/// </summary>
	/// 
	/// <remarks>
	/// While most of the generation happens in parallel, this function also requires some time on the main thread.
	/// If you need to update many tiles, consider spreading the updates accross multiple frames.
	/// </remarks>
	public async Task GenerateTile( PhysicsWorld world, Vector3 worldPosition )
	{
		ThreadSafe.AssertIsMainThread();

		if ( !IsEnabled )
			await Task.CompletedTask;

		var tilePosition = WorldPositionToTilePosition( worldPosition );
		var tile = tileCache.GetOrAddTile( tilePosition );

		var generatorConfig = CreateTileGenerationConfig( tile.TilePosition );
		var heightFieldGenerator = HeightFieldGeneratorPool.Get();
		heightFieldGenerator.Init( generatorConfig );
		heightFieldGenerator.CollectGeometry( this, world, generatorConfig.Bounds );

		var data = await Task.Run( () =>
		{
			CompactHeightfield heightField = null;
			try
			{
				heightField = heightFieldGenerator.Generate();

				tile.SetCachedHeightField( heightField );

				if ( heightField == null )
				{
					return null;
				}

				tile.HeightfieldBuildComplete();

				return tile.BuildNavmesh( heightField, generatorConfig, this );
			}
			finally
			{
				HeightFieldGeneratorPool.Return( heightFieldGenerator );
				heightField?.Dispose();
			}
		} );

		if ( data != null )
		{
			LoadTileOnMainThread( tile, data );
		}
	}

	/// <summary>
	/// Generates or regenerates the navmesh tiles overlapping with the given bounds.
	/// This function is thread safe but can only be called from the main thread.
	/// </summary>
	/// 
	/// <remarks>
	/// While most of the generation happens in parallel, this function also requires some time on the main thread.
	/// If you need to update many tiles, consider spreading the updates accross multiple frames.
	/// </remarks>
	public async Task GenerateTiles( PhysicsWorld world, BBox bounds )
	{
		ThreadSafe.AssertIsMainThread();

		if ( !IsEnabled )
			await Task.CompletedTask;

		var minMaxTileCoords = CalculateMinMaxTileCoords( bounds );

		if ( minMaxTileCoords.Width <= 0 || minMaxTileCoords.Height <= 0 ) return;

		var tilesToProcess = new List<NavMeshTile>( minMaxTileCoords.Width * minMaxTileCoords.Height );
		for ( int x = minMaxTileCoords.Left; x <= minMaxTileCoords.Right; x++ )
		{
			for ( int y = minMaxTileCoords.Top; y <= minMaxTileCoords.Bottom; y++ )
			{
				tilesToProcess.Add( tileCache.GetOrAddTile( new Vector2Int( x, y ) ) );
			}
		}

		var maxConcurrency = Math.Max( 1, HeightFieldGenerationThreadCount );
		using var concurrencySemaphore = new SemaphoreSlim( maxConcurrency, maxConcurrency );
		var generationTasks = new List<Task>( tilesToProcess.Count );

		foreach ( var tile in tilesToProcess )
		{
			generationTasks.Add( ProcessTileAsync( tile ) );
		}

		await Task.WhenAll( generationTasks );

		async Task ProcessTileAsync( NavMeshTile tile )
		{
			await concurrencySemaphore.WaitAsync().ConfigureAwait( false );
			try
			{
				await Task.Run( async () =>
				{
					var generatorConfig = CreateTileGenerationConfig( tile.TilePosition );

					try
					{
						var heightFieldGenerator = HeightFieldGeneratorPool.Get();
						CompactHeightfield heightField;
						try
						{
							heightFieldGenerator.Init( generatorConfig );

							await GameTask.MainThread();
							heightFieldGenerator.CollectGeometry( this, world, generatorConfig.Bounds );
							await GameTask.WorkerThread();

							heightField = heightFieldGenerator.Generate();
						}
						finally
						{
							HeightFieldGeneratorPool.Return( heightFieldGenerator );
						}

						tile.SetCachedHeightField( heightField );

						if ( heightField == null )
						{
							return;
						}

						tile.HeightfieldBuildComplete();

						using ( heightField )
						{
							var tileMesh = tile.BuildNavmesh( heightField, generatorConfig, this );

							await GameTask.MainThread();
							LoadTileOnMainThread( tile, tileMesh );
						}
					}
					catch ( Exception e )
					{
						// Swallow per-tile exceptions to avoid cancelling whole batch;
						Log.Warning( $"Navmesh: Exception while generating tile {tile.TilePosition.x},{tile.TilePosition.y}" );
						Log.Warning( e );
					}
				} ).ConfigureAwait( false );
			}
			finally
			{
				concurrencySemaphore.Release();
			}
		}
	}

	internal void LoadTileOnMainThread( NavMeshTile targetTile, DtMeshData data )
	{
		ThreadSafe.AssertIsMainThread();

		var tileRef = navmeshInternal.GetTileRefAt( targetTile.TilePosition.x, targetTile.TilePosition.y, 0 );

		if ( data == null )
		{
			if ( tileRef != default )
			{
				navmeshInternal.RemoveTile( tileRef );
			}
			return;
		}

		if ( tileRef != default )
		{
			navmeshInternal.UpdateTile( data, 0 );
		}
		else
		{
			navmeshInternal.AddTile( data, 0, 0, out var _ );
		}

		targetTile.UpdateLinkStatus( this );
	}

	internal void UnloadTileOnMainThread( Vector2Int tilePosition )
	{
		ThreadSafe.AssertIsMainThread();

		var tileRef = navmeshInternal.GetTileRefAt( tilePosition.x, tilePosition.y, 0 );

		if ( tileRef != default )
		{
			navmeshInternal.RemoveTile( tileRef );
		}
	}

	internal Vector2Int WorldPositionToTilePosition( Vector3 worldPosition )
	{
		var tileLocationFloat = (worldPosition - TileOrigin) / TileSizeWorldSpace;
		return new Vector2Int( (int)tileLocationFloat.x, (int)tileLocationFloat.y );
	}

	internal Vector3 TilePositionToWorldPosition( Vector2Int tilePosition )
	{
		return TileOrigin + new Vector3( tilePosition.x, tilePosition.y, 0 ) * TileSizeWorldSpace + TileSizeWorldSpace * 0.5f;
	}

	internal RectInt CalculateMinMaxTileCoords( BBox bounds )
	{
		var clampedMins = Vector3.Max( bounds.Mins, WorldBounds.Mins );
		var clampedMaxs = Vector3.Min( bounds.Maxs, WorldBounds.Maxs );

		var coordMin = WorldPositionToTilePosition( clampedMins );
		var coordMax = WorldPositionToTilePosition( clampedMaxs );

		return new RectInt( coordMin, coordMax - coordMin );
	}

	internal BBox CalculateTileBounds( Vector2Int tilePosition )
	{
		return BBox.FromPositionAndSize( TilePositionToWorldPosition( tilePosition ), TileSizeWorldSpace );
	}

	internal Config CreateTileGenerationConfig( Vector2Int tilePosition )
	{
		var tileBoundsWorld = CalculateTileBounds( tilePosition );

		var cfg = Config.CreateValidatedConfig(
			tilePosition,
			tileBoundsWorld,
			CellSize,
			CellHeight,
			AgentHeight,
			AgentRadius,
			AgentStepSize,
			AgentMaxSlope
		);

		return cfg;
	}
}
