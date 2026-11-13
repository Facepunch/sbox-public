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

	// The scene refers to a baked file that held nothing the navmesh could use
	private bool bakeMissing;
	private CancellationTokenSource editorBake;

	// Bakes waiting for the navmesh to finish. Auto update would mark every tile again each frame.
	private int bakesWaiting;

	/// <summary>
	/// An editor scene that draws its navmesh rebuilds it every update, unless a bake is waiting for it.
	/// </summary>
	internal bool AutoUpdates => DrawMesh && EditorAutoUpdate && bakesWaiting == 0;

	/// <summary>
	/// True once after a scene was loaded whose baked file was empty, damaged or made for an older
	/// version or other agent settings.
	/// </summary>
	internal bool TakeMissingBake()
	{
		bool missing = bakeMissing;
		bakeMissing = false;
		return missing;
	}

	[Obsolete( "The navmesh is baked whenever its scene is saved in the editor" )]
	public static void BakeNavMesh()
	{
		if ( Application.IsEditor && Application.Editor.Scene is { Editor: { } editor } scene )
			_ = scene.NavMesh.BakeInEditor( scene.PhysicsWorld, editor.GetSceneFolder(), () => !editor.HasUnsavedChanges, regenerate: true );
	}

	/// <summary>
	/// The editor's bake: the scene refers to the baked file from now on, and the file is empty until
	/// the navmesh is finished, so a bake that is interrupted leaves a scene that generates when it
	/// loads. Nothing is written unless unedited() still holds then: what was built would no longer
	/// be the scene as it was saved. regenerate builds every tile from the world as it is now first.
	/// </summary>
	internal Task BakeInEditor( PhysicsWorld world, SceneFolder folder, Func<bool> unedited, bool regenerate )
	{
		editorBake?.Cancel();
		editorBake = null;
		if ( !IsEnabled || folder is null )
		{
			_bakedDataPath = null;
			return Task.CompletedTask;
		}

		_bakedDataPath = folder.WriteFile( _navDataFilename, [] );
		editorBake = new CancellationTokenSource();
		return WriteBake( world, folder, unedited, regenerate, editorBake.Token );
	}

	private async Task WriteBake( PhysicsWorld world, SceneFolder folder, Func<bool> unedited, bool regenerate, CancellationToken cancelToken )
	{
		try
		{
			if ( regenerate ) SetDirty();
			var data = await BakeDataToBytes( cancelToken, world );
			await GameTask.MainThread();
			if ( data is null || cancelToken.IsCancellationRequested || !unedited() ) return;

			folder.WriteFile( _navDataFilename, data );
		}
		catch ( OperationCanceledException )
		{
		}
		catch ( Exception e )
		{
			Log.Warning( e, "NavMesh: Failed to bake" );
		}
	}

	/// <summary>
	/// Bakes the navmesh tile data to a byte array. Returns null if there are no tiles to bake.
	/// Waits for pending tiles, and builds them when given the world.
	/// </summary>
	internal async Task<byte[]> BakeDataToBytes( CancellationToken cancelToken = default, PhysicsWorld world = null )
	{
		ThreadSafe.AssertIsMainThread();

		bakesWaiting++;
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

			return cancelToken.IsCancellationRequested ? null : baked;
		}
		finally
		{
			bakesWaiting--;
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
			Log.Warning( "NavMesh: Baked data is in an older format and is ignored. Saving the scene in the editor bakes it again." );
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

			if ( problem is null )
			{
				if ( !Nav.NavMesh.TryReadSettings( raw, out var baked ) ) problem = "is in an older format";
				else if ( !baked.Equals( settings ) ) problem = "was made with other agent settings";
			}
			if ( problem is not null ) return null;

			var mesh = Nav.NavMesh.Load( raw, cacheCodec );
			if ( mesh is null ) problem = "is damaged";
			return mesh;
		} );
		await GameTask.MainThread();

		if ( generation != Generation || disposed ) return;
		if ( loaded is null )
		{
			Log.Warning( $"NavMesh: Baked data {problem} and is ignored. Saving the scene in the editor bakes it again." );
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
