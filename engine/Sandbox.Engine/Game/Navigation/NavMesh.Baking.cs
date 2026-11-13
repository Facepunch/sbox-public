using Editor;
using Sandbox.Compression;
using System.Buffers;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Sandbox.Navigation;

public sealed partial class NavMesh
{
	// Header, then the library's bake as one LZ4 block. A tile inside the baked rectangle that the bake lacks is empty on purpose.
	//   uint magic, uint version, int minTileX, minTileY, maxTileX, maxTileY, int uncompressedLength
	private const uint BakeMagic = 0x56414E53;
	private const uint BakeVersion = 1;
	private const int BakeHeaderSize = 28;

	// The tiles the loaded bake covers
	private RectInt? bakedTiles;

	[Menu( "Editor", "Scene/Bake NavMesh", "directions_run", Priority = 1800 )]
	public static async void BakeNavMesh()
	{
		if ( !Application.IsEditor )
		{
			Log.Warning( "NavMesh Baking is only available in editor." );
			return;
		}

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
				progress.Subtitle = "Compressing...";
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
		var data = await BakeDataToBytes( onProgress, cancelToken, scene?.PhysicsWorld );
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
	/// Used internally by BakeData and for testing. Waits for pending tiles, and builds them when given the world.
	/// </summary>
	internal async Task<byte[]> BakeDataToBytes( Action<int, int> onProgress = null, CancellationToken cancelToken = default, PhysicsWorld world = null )
	{
		ThreadSafe.AssertIsMainThread();

		// Auto update marks every tile again each frame, which would keep the wait below from ending
		var wasAutoUpdate = EditorAutoUpdate;
		EditorAutoUpdate = false;
		try
		{
			// A tile the bake lacks counts as empty on purpose at load, so nothing may be missing yet
			while ( live is not null && (IsGenerating || IsDirty || next is not null || live.Mesh.PendingTiles > 0) )
			{
				if ( cancelToken.IsCancellationRequested ) return null;

				if ( world is not null && !IsGenerating )
				{
					if ( IsDirty ) InvalidateAllTiles( world );
					UpdateCache( world );
				}

				await Task.Delay( 10 );
				await GameTask.MainThread();
			}

			Span<(int, int)> any = stackalloc (int, int)[1];
			if ( live is null || live.Mesh.GetTiles( any ) == 0 ) return null;

			var tiles = CalculateMinMaxTileCoords( Bounds );
			var writer = new ArrayBufferWriter<byte>();
			if ( !live.Mesh.Save( writer ) )
			{
				Log.Warning( "NavMesh: the navmesh is too large to bake" );
				return null;
			}
			onProgress?.Invoke( 0, 1 );

			var baked = await Task.Run( () =>
			{
				var raw = writer.WrittenSpan;
				var data = new byte[BakeHeaderSize + LZ4.MaxCompressedSize( raw.Length )];
				BinaryPrimitives.WriteUInt32LittleEndian( data, BakeMagic );
				BinaryPrimitives.WriteUInt32LittleEndian( data.AsSpan( 4 ), BakeVersion );
				BinaryPrimitives.WriteInt32LittleEndian( data.AsSpan( 8 ), tiles.Left );
				BinaryPrimitives.WriteInt32LittleEndian( data.AsSpan( 12 ), tiles.Top );
				BinaryPrimitives.WriteInt32LittleEndian( data.AsSpan( 16 ), tiles.Right );
				BinaryPrimitives.WriteInt32LittleEndian( data.AsSpan( 20 ), tiles.Bottom );
				BinaryPrimitives.WriteInt32LittleEndian( data.AsSpan( 24 ), raw.Length );
				int length = LZ4.CompressBlock( raw, data.AsSpan( BakeHeaderSize ), CompressionLevel.Optimal );
				return data.AsSpan( 0, BakeHeaderSize + length ).ToArray();
			} );

			onProgress?.Invoke( 1, 1 );
			return cancelToken.IsCancellationRequested ? null : baked;
		}
		finally
		{
			EditorAutoUpdate = wasAutoUpdate;
		}
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
	/// Data in an older format, damaged, or baked with other agent settings is ignored with a warning.
	/// </summary>
	internal async Task LoadFromBakedData( byte[] bakedData )
	{
		if ( bakedData is null || bakedData.Length == 0 )
			return;

		int rawLength = bakedData.Length >= BakeHeaderSize ? BinaryPrimitives.ReadInt32LittleEndian( bakedData.AsSpan( 24 ) ) : -1;
		if ( rawLength < 0 || BinaryPrimitives.ReadUInt32LittleEndian( bakedData ) != BakeMagic || BinaryPrimitives.ReadUInt32LittleEndian( bakedData.AsSpan( 4 ) ) != BakeVersion
			|| rawLength / 255 > bakedData.Length )
		{
			Log.Warning( "NavMesh: Baked data is in an older format and is ignored. Bake the navmesh again." );
			return;
		}

		var min = new Vector2Int( BinaryPrimitives.ReadInt32LittleEndian( bakedData.AsSpan( 8 ) ), BinaryPrimitives.ReadInt32LittleEndian( bakedData.AsSpan( 12 ) ) );
		var max = new Vector2Int( BinaryPrimitives.ReadInt32LittleEndian( bakedData.AsSpan( 16 ) ), BinaryPrimitives.ReadInt32LittleEndian( bakedData.AsSpan( 20 ) ) );

		long generation = Generation;
		var settings = CreateSettings();
		string problem = null;

		var loaded = await Task.Run( () =>
		{
			var raw = new byte[rawLength];
			try
			{
				if ( LZ4.DecompressBlock( bakedData.AsSpan( BakeHeaderSize ), raw ) != rawLength ) problem = "is damaged";
			}
			catch ( InvalidDataException )
			{
				problem = "is damaged";
			}

			if ( problem is null && (!Nav.NavMesh.TryReadSettings( raw, out var baked ) || !baked.Equals( settings )) ) problem = "was made with other agent settings";
			if ( problem is not null ) return null;

			var mesh = Nav.NavMesh.Load( raw, cacheCodec );
			if ( mesh is null ) problem = "is damaged";
			return mesh;
		} );
		await GameTask.MainThread();

		if ( generation != Generation || disposed ) return;
		if ( loaded is null )
		{
			Log.Warning( $"NavMesh: Baked data {problem} and is ignored. Bake the navmesh again." );
			return;
		}

		SetMesh( loaded );
		bakedTiles = new RectInt( min, max - min );
	}

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
