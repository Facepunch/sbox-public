using Editor;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Threading;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Sandbox.Navigation;

public sealed partial class NavMesh
{
	[Menu( "Editor", "Scene/Bake NavMesh", "directions_run", Priority = 1800 )]
	public static async void BakeNavMesh()
	{
		if ( !Application.IsEditor ) throw new InvalidOperationException( "NavMesh Baking is only available in editor." );

		var navMesh = Application.Editor.Scene.NavMesh;

		using var progress = Application.Editor.ProgressSection();
		progress.Title = "Baking NavMesh";
		progress.Subtitle = "Collecting tiles...";

		await navMesh.BakeData(
			scene: Application.Editor.Scene,
			onProgress: ( current, total ) =>
			{
				progress.Current = current;
				progress.TotalCount = total;
				progress.Subtitle = $"Processing chunk {current} / {total}";
			},
			cancelToken: progress.GetCancel()
		);

		progress.Subtitle = "Complete!";
	}

	/// <summary>
	/// Core baking logic without editor dependencies. Used by BakeDataAsync and tests.
	/// </summary>
	internal async Task BakeData( Scene scene, Action<int, int> onProgress = null, CancellationToken cancelToken = default )
	{
		var data = await BakeDataToBytes( onProgress, cancelToken );
		if ( data is null )
		{
			_bakedDataPath = null;
			return;
		}

		// Save baked data to navdata_c file
		var sceneFolder = scene?.Editor?.GetSceneFolder();
		if ( sceneFolder is null )
		{
			Log.Warning( "NavMesh: Cannot save baked data - no scene folder available" );
			return;
		}

		scene?.Editor?.HasUnsavedChanges = true;

		_bakedDataPath = sceneFolder.WriteFile( _navDataFilename, data );
		Log.Info( $"NavMesh: Saved baked data to {_bakedDataPath} ({data.Length} bytes)" );
	}

	/// <summary>
	/// Bakes the navmesh tile data to a byte array. Returns null if there are no tiles to bake.
	/// Used internally by BakeData and for testing.
	/// </summary>
	internal async Task<byte[]> BakeDataToBytes( Action<int, int> onProgress = null, CancellationToken cancelToken = default )
	{
		// Suspend auto-update so it doesn't dispatch new heightfield builds while we bake.
		// Without this, WaitForHeightfieldBuilds would never finish because new builds
		// are dispatched every frame.
		var wasAutoUpdate = EditorAutoUpdate;
		EditorAutoUpdate = false;

		// Wait for any in-progress heightfield builds to finish first.
		// Without this, SetCachedHeightField can mutate tiles concurrently during the bake.
		await WaitForHeightfieldBuilds( cancelToken );

		if ( cancelToken.IsCancellationRequested )
		{
			EditorAutoUpdate = wasAutoUpdate;
			return null;
		}

		var minMaxTileCoords = CalculateMinMaxTileCoords( Bounds );

		// Need one iteration to find out actual tile count
		List<NavMeshTile> tiles = new();

		for ( int x = minMaxTileCoords.Left; x <= minMaxTileCoords.Right; x++ )
		{
			for ( int y = minMaxTileCoords.Top; y <= minMaxTileCoords.Bottom; y++ )
			{
				var tileCoords = new Vector2Int( x, y );
				var tile = tileCache.GetOrAddTile( tileCoords );
				if ( tile.IsHeightFieldValid )
				{
					tiles.Add( tile );
				}
			}
		}

		if ( tiles.Count == 0 )
		{
			EditorAutoUpdate = wasAutoUpdate;
			return null;
		}

		var chunkCount = (tiles.Count + TilesPerChunk - 1) / TilesPerChunk;
		var compressedChunks = new byte[chunkCount][];
		int completedChunks = 0;

		onProgress?.Invoke( 0, chunkCount );

		await Task.Run( () =>
		{
			Parallel.For( 0, chunkCount, TileParallelOptions, chunkIndex =>
			{
				if ( cancelToken.IsCancellationRequested )
					return;

				var start = chunkIndex * TilesPerChunk;
				var chunkTileCount = Math.Min( TilesPerChunk, tiles.Count - start );

				using var chunkStream = ByteStream.Create( 1024 );
				chunkStream.Write( chunkTileCount );

				for ( int i = 0; i < chunkTileCount; i++ )
				{
					var tile = tiles[start + i];
					chunkStream.Write( tile.TilePosition );
					chunkStream.WriteArray( tile.CopyCompressedHeightField().AsSpan() );
				}

				using var compressedStream = chunkStream.Compress( CompressionLevel.SmallestSize );
				compressedChunks[chunkIndex] = compressedStream.ToArray();

				var current = Interlocked.Increment( ref completedChunks );
				onProgress?.Invoke( current, chunkCount );
			} );
		} );

		// Restore auto-update now that bake is done
		EditorAutoUpdate = wasAutoUpdate;

		if ( cancelToken.IsCancellationRequested )
			return null;

		// Calculate total size needed
		var totalSize = sizeof( int ); // chunk count
		foreach ( var chunk in compressedChunks )
		{
			totalSize += sizeof( int ) + chunk.Length; // length prefix + data
		}

		// Write all chunks into a single byte array
		using var outputStream = ByteStream.Create( totalSize );
		outputStream.Write( chunkCount );
		foreach ( var chunk in compressedChunks )
		{
			outputStream.WriteArray( chunk );
		}

		return outputStream.ToArray();
	}

	internal async Task LoadFromBake()
	{
		long generation = Generation;
		if ( string.IsNullOrEmpty( _bakedDataPath ) )
			return;

		byte[] bakedData;
		try
		{
			bakedData = await FileSystem.Mounted.ReadAllBytesAsync( _bakedDataPath );
		}
		catch ( Exception ex )
		{
			Log.Warning( $"NavMesh: Failed to load baked data from {_bakedDataPath}: {ex.Message}" );
			return;
		}

		if ( bakedData.Length == 0 || generation != Generation || disposed )
			return;

		Log.Info( $"NavMesh: Loaded baked data from {_bakedDataPath} ({bakedData.Length} bytes)" );

		await LoadFromBakedData( bakedData );
	}

	/// <summary>
	/// Loads navmesh tile data from a byte array. Used internally by LoadFromBake and for testing.
	/// </summary>
	internal async Task LoadFromBakedData( byte[] bakedData )
	{
		if ( bakedData is null || bakedData.Length == 0 )
			return;

		// Parse chunk offsets from the data before await - avoid per-chunk allocations
		var chunkCount = MemoryMarshal.Read<int>( bakedData );
		if ( chunkCount <= 0 )
			return;

		// Calculate offsets for each chunk in the original data
		var chunkOffsets = new (int Start, int Length)[chunkCount];

		int offset = sizeof( int ); // Skip chunk count
		for ( int i = 0; i < chunkCount; i++ )
		{
			var length = MemoryMarshal.Read<int>( bakedData.AsSpan( offset ) );
			offset += sizeof( int );
			chunkOffsets[i] = (offset, length);
			offset += length;
		}

		long generation = Generation;

		await Task.Run( () =>
		{
			Parallel.For( 0, chunkOffsets.Length, TileParallelOptions, chunkIndex =>
			{
				var (start, length) = chunkOffsets[chunkIndex];
				using var compressedStream = ByteStream.CreateReader( bakedData.AsSpan( start, length ) );
				using var chunkStream = compressedStream.Decompress();

				var tileCount = chunkStream.Read<int>();

				for ( int i = 0; i < tileCount; i++ )
				{
					var tilePosition = chunkStream.Read<Vector2Int>();
					var heightFieldData = chunkStream.ReadArray<byte>();

					lock ( SyncRoot )
					{
						if ( generation != Generation || disposed ) return;
						var tile = tileCache.GetOrAddTile( tilePosition );
						tile.SetCompressedHeightField( heightFieldData );
					}
				}
			} );
		} );
	}

	/// <summary>
	/// Waits for all in-progress heightfield builds to complete.
	/// This ensures we snapshot a consistent state before baking.
	/// </summary>
	private async Task WaitForHeightfieldBuilds( CancellationToken cancelToken )
	{
		while ( !cancelToken.IsCancellationRequested && tileCache.HasAnyBuildsInProgress() )
		{
			await Task.Delay( 50, cancelToken ).ConfigureAwait( false );
		}
	}

	private const int TilesPerChunk = 64;

	private static readonly ParallelOptions TileParallelOptions = new()
	{
		MaxDegreeOfParallelism = Math.Max( 1, Environment.ProcessorCount - 1 )
	};

	/// <summary>
	/// The relative path to the baked navdata file (without _c suffix)
	/// </summary>
	private string _bakedDataPath;

	/// <summary>
	/// Get the navdata filename for this scene
	/// </summary>
	private readonly string _navDataFilename = "/navmesh/baked.navdata";

	/// <summary>
	/// Data saved in a Scene file
	/// </summary>
	internal JsonObject Serialize()
	{
		JsonObject jso = new JsonObject();

		jso["Enabled"] = IsEnabled;
		jso["IncludeStaticBodies"] = IncludeStaticBodies;
		jso["IncludeKeyframedBodies"] = IncludeKeyframedBodies;
		jso["EditorAutoUpdate"] = EditorAutoUpdate;
		jso["AgentHeight"] = AgentHeight;
		jso["AgentRadius"] = AgentRadius;
		jso["AgentStepSize"] = AgentStepSize;
		jso["AgentMaxSlope"] = AgentMaxSlope;
		jso["ExcludedBodies"] = Json.ToNode( ExcludedBodies, typeof( TagSet ) );
		jso["IncludedBodies"] = Json.ToNode( IncludedBodies, typeof( TagSet ) );
		jso["DeferGeneration"] = DeferGeneration;
		jso["CustomBounds"] = CustomBounds;
		if ( CustomBounds ) jso["Bounds"] = Json.ToNode( Bounds, typeof( BBox ) );

		// Store reference to the baked data file as a RawFileReference
		if ( !string.IsNullOrWhiteSpace( _bakedDataPath ) )
		{
			jso["BakedDataPath"] = JsonSerializer.SerializeToNode( FileReference.FromPath( _bakedDataPath ) );
		}

		return jso;
	}

	/// <summary>
	/// Data loaded from a Scene file
	/// </summary>
	internal void Deserialize( JsonObject jso )
	{
		if ( jso is null )
			return;

		IncludeStaticBodies = (bool)(jso["IncludeStaticBodies"] ?? IncludeStaticBodies);
		IncludeKeyframedBodies = (bool)(jso["IncludeKeyframedBodies"] ?? IncludeKeyframedBodies);
		EditorAutoUpdate = (bool)(jso["EditorAutoUpdate"] ?? EditorAutoUpdate);
		AgentHeight = (float)(jso["AgentHeight"] ?? AgentHeight);
		AgentRadius = (float)(jso["AgentRadius"] ?? AgentRadius);
		AgentStepSize = (float)(jso["AgentStepSize"] ?? AgentStepSize);
		AgentMaxSlope = (float)(jso["AgentMaxSlope"] ?? AgentMaxSlope);

		ExcludedBodies = Json.FromNode<TagSet>( jso["ExcludedBodies"] ) ?? ExcludedBodies;
		IncludedBodies = Json.FromNode<TagSet>( jso["IncludedBodies"] ) ?? IncludedBodies;
		DeferGeneration = (bool)(jso["DeferGeneration"] ?? DeferGeneration);
		CustomBounds = (bool)(jso["CustomBounds"] ?? CustomBounds);
		Bounds = CustomBounds && jso["Bounds"] is not null ? Json.FromNode<BBox>( jso["Bounds"] ) : default;

		// Load baked data path from RawFileReference
		if ( jso["BakedDataPath"] is JsonObject bakedDataObj )
		{
			_bakedDataPath = bakedDataObj["path"]?.ToString();
		}
		else _bakedDataPath = null;
		IsEnabled = (bool)(jso["Enabled"] ?? false);
	}
}
