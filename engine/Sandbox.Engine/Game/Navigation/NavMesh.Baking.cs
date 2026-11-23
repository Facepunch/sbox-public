using Editor;
using Nav;
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
	// Header, then blocks to the end: the library's bake, then the generated links. A tile inside the baked rectangle that the bake lacks is empty on purpose.
	//   uint magic, uint version, int minTileX, minTileY, maxTileX, maxTileY
	//   Per block: int rawLength, int storedLength, the LZ4 block
	//   Links: float maxDrop, maxJumpDistance, maxJumpUp, spacing, int tiles, per tile int x, y, links, per link float start xyz, end xyz, radius, byte 1 two-way | 2 jump
	// Versions 1 and 2 held a library bake of an older format.
	private const uint BakeMagic = 0x56414E53;
	private const uint BakeVersion = 3;
	private const int BakeHeaderSize = 24;

	// The tiles the loaded bake covers
	private RectInt? bakedTiles;

	// The scene refers to a baked file that held nothing the navmesh could use
	private bool bakeMissing;
	private CancellationTokenSource editorBake;
	private Task<bool> editorBakeTask;

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
	internal Task<bool> BakeInEditor( PhysicsWorld world, SceneFolder folder, Func<bool> unedited, bool regenerate )
	{
		if ( !IsEnabled || folder is null )
		{
			editorBake?.Cancel();
			_bakedDataPath = null;
			return editorBakeTask = Task.FromResult( false );
		}

		_bakedDataPath = folder.WriteFile( BakeFilename, [] );
		return StartBake( world, folder, unedited, regenerate, default );
	}

	Task<bool> StartBake( PhysicsWorld world, SceneFolder folder, Func<bool> unedited, bool regenerate, CancellationToken cancel )
	{
		editorBake?.Cancel();
		editorBake = CancellationTokenSource.CreateLinkedTokenSource( cancel );
		return editorBakeTask = WriteBake( world, folder, unedited, regenerate, editorBake );
	}

	internal void ClearBake( Scene scene )
	{
		editorBake?.Cancel();

		if ( _bakedDataPath is null )
			return;

		_bakedDataPath = null;
		scene.Editor.MarkBakeReferencesChanged();
	}

	internal async Task BakeForCompile( Scene scene, SceneFolder folder, CancellationToken cancel, bool clean )
	{
		var previousPath = _bakedDataPath;
		if ( clean || editorBakeTask is not { IsCompleted: false } pending || !await pending.WaitAsync( cancel ) )
		{
			cancel.ThrowIfCancellationRequested();
			if ( !await StartBake( scene.PhysicsWorld, folder, () => scene.IsValid() && IsEnabled, regenerate: true, cancel ) )
			{
				cancel.ThrowIfCancellationRequested();
				throw new InvalidOperationException( "Navigation mesh bake produced no data." );
			}
		}

		if ( previousPath != _bakedDataPath )
			scene.Editor.MarkBakeReferencesChanged();
	}

	private async Task<bool> WriteBake( PhysicsWorld world, SceneFolder folder, Func<bool> unedited, bool regenerate, CancellationTokenSource cancellation )
	{
		var cancelToken = cancellation.Token;
		try
		{
			if ( regenerate ) SetDirty();
			var data = await BakeDataToBytes( cancelToken, world );
			await GameTask.MainThread();
			if ( data is null || cancelToken.IsCancellationRequested || !unedited() ) return false;

			_bakedDataPath = folder.WriteFile( BakeFilename, data );
			return true;
		}
		catch ( OperationCanceledException )
		{
			return false;
		}
		catch ( Exception e )
		{
			Log.Warning( e, "NavMesh: Failed to bake" );
			return false;
		}
		finally
		{
			if ( editorBake == cancellation )
				editorBake = null;
			cancellation.Dispose();
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
			var links = SaveGeneratedLinks();

			var baked = await Task.Run( () =>
			{
				var raw = writer.WrittenSpan;
				var data = new byte[BakeHeaderSize + 16 + LZ4.MaxCompressedSize( raw.Length ) + LZ4.MaxCompressedSize( links.Length )];
				BinaryPrimitives.WriteUInt32LittleEndian( data, BakeMagic );
				BinaryPrimitives.WriteUInt32LittleEndian( data.AsSpan( 4 ), BakeVersion );
				BinaryPrimitives.WriteInt32LittleEndian( data.AsSpan( 8 ), tiles.Left );
				BinaryPrimitives.WriteInt32LittleEndian( data.AsSpan( 12 ), tiles.Top );
				BinaryPrimitives.WriteInt32LittleEndian( data.AsSpan( 16 ), tiles.Right );
				BinaryPrimitives.WriteInt32LittleEndian( data.AsSpan( 20 ), tiles.Bottom );
				int length = BakeHeaderSize;
				length += WriteBlock( raw, data.AsSpan( length ) );
				length += WriteBlock( links, data.AsSpan( length ) );
				return data.AsSpan( 0, length ).ToArray();
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

		uint version = bakedData.Length >= BakeHeaderSize + 4 && BinaryPrimitives.ReadUInt32LittleEndian( bakedData ) == BakeMagic ? BinaryPrimitives.ReadUInt32LittleEndian( bakedData.AsSpan( 4 ) ) : 0;
		if ( version != BakeVersion )
		{
			Log.Warning( "NavMesh: Baked data is in an older format and is ignored. Saving the scene in the editor bakes it again." );
			return;
		}

		var min = new Vector2Int( BinaryPrimitives.ReadInt32LittleEndian( bakedData.AsSpan( 8 ) ), BinaryPrimitives.ReadInt32LittleEndian( bakedData.AsSpan( 12 ) ) );
		var max = new Vector2Int( BinaryPrimitives.ReadInt32LittleEndian( bakedData.AsSpan( 16 ) ), BinaryPrimitives.ReadInt32LittleEndian( bakedData.AsSpan( 20 ) ) );

		long generation = Generation;
		var settings = CreateSettings();
		var options = CreateMeshOptions( settings );
		string problem = null;
		Dictionary<(int X, int Y), List<NavLink>> links = null;

		var loaded = await Task.Run( () =>
		{
			int offset = BakeHeaderSize;
			var raw = ReadBlock( bakedData, ref offset );
			var linkRaw = raw is null ? null : ReadBlock( bakedData, ref offset );
			if ( raw is null || linkRaw is null ) problem = "is damaged";
			else if ( !Nav.NavMesh.TryReadSettings( raw, out var baked ) ) problem = "is in an older format";
			else if ( !baked.Equals( settings ) ) problem = "was made with other agent settings";
			else problem = ReadGeneratedLinks( linkRaw, options.Links, out links );
			if ( problem is not null ) return null;

			var mesh = Nav.NavMesh.Load( raw, options );
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

		SetMesh( loaded, links );
		bakedTiles = new RectInt( min, max - min );
	}

	internal static int WriteBlock( ReadOnlySpan<byte> raw, Span<byte> target )
	{
		int stored = LZ4.CompressBlock( raw, target[8..], CompressionLevel.Optimal );
		BinaryPrimitives.WriteInt32LittleEndian( target, raw.Length );
		BinaryPrimitives.WriteInt32LittleEndian( target[4..], stored );
		return 8 + stored;
	}

	// Null when damaged
	internal static byte[] ReadBlock( byte[] data, ref int offset )
	{
		if ( data.Length - offset < 8 ) return null;
		int rawLength = BinaryPrimitives.ReadInt32LittleEndian( data.AsSpan( offset ) );
		int stored = BinaryPrimitives.ReadInt32LittleEndian( data.AsSpan( offset + 4 ) );
		offset += 8;
		if ( rawLength < 0 || stored < 0 || stored > data.Length - offset || rawLength / 255 > stored ) return null;

		var raw = new byte[rawLength];
		try
		{
			if ( LZ4.DecompressBlock( data.AsSpan( offset, stored ), raw ) != rawLength ) return null;
		}
		catch ( InvalidDataException )
		{
			return null;
		}
		offset += stored;
		return raw;
	}

	// The generated links of the mesh in use, by tile
	private byte[] SaveGeneratedLinks()
	{
		var settings = live.Mesh.LinkSettings;
		using var stream = new MemoryStream();
		using var writer = new BinaryWriter( stream );
		writer.Write( settings.MaxDrop );
		writer.Write( settings.MaxJumpDistance );
		writer.Write( settings.MaxJumpUp );
		writer.Write( settings.Spacing );
		writer.Write( generatedLinks.Count );
		foreach ( var ((x, y), components) in generatedLinks )
		{
			writer.Write( x );
			writer.Write( y );
			writer.Write( components.Count );
			foreach ( var component in components )
			{
				var link = component.generated;
				writer.Write( link.Start.X );
				writer.Write( link.Start.Y );
				writer.Write( link.Start.Z );
				writer.Write( link.End.X );
				writer.Write( link.End.Y );
				writer.Write( link.End.Z );
				writer.Write( link.Radius );
				writer.Write( (byte)((link.Bidirectional ? 1 : 0) | (link.Type != settings.DropType ? 2 : 0)) );
			}
		}
		writer.Flush();
		return stream.ToArray();
	}

	// Null when the links were generated with these settings. Anything malformed rejects them all.
	static string ReadGeneratedLinks( byte[] raw, in NavLinkSettings settings, out Dictionary<(int X, int Y), List<NavLink>> links )
	{
		links = new();
		string problem = null;
		using var reader = new BinaryReader( new MemoryStream( raw ) );
		try
		{
			if ( reader.ReadSingle() != settings.MaxDrop || reader.ReadSingle() != settings.MaxJumpDistance || reader.ReadSingle() != settings.MaxJumpUp || reader.ReadSingle() != settings.Spacing )
				problem = "was made with other agent settings";
			else problem = ReadTiles( reader, raw.Length, settings, links ) ? null : "is damaged";
		}
		catch ( EndOfStreamException )
		{
			problem = "is damaged";
		}

		if ( problem is not null ) links = null;
		return problem;
	}

	// A tile takes at least 12 bytes and a link 29
	static bool ReadTiles( BinaryReader reader, int length, in NavLinkSettings settings, Dictionary<(int X, int Y), List<NavLink>> links )
	{
		int tiles = reader.ReadInt32();
		if ( tiles < 0 || tiles > (length - reader.BaseStream.Position) / 12 ) return false;
		for ( ; tiles > 0; tiles-- )
		{
			var tile = (reader.ReadInt32(), reader.ReadInt32());
			int count = reader.ReadInt32();
			if ( count < 0 || count > (length - reader.BaseStream.Position) / 29 || !links.TryAdd( tile, new List<NavLink>( count ) ) ) return false;
			for ( int i = 0; i < count; i++ )
			{
				// As the library reports them
				var link = new NavLink
				{
					Start = new( reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle() ),
					End = new( reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle() ),
					Radius = reader.ReadSingle(),
					Area = NavFilter.DefaultArea
				};
				byte flags = reader.ReadByte();
				if ( !float.IsFinite( link.Start.X + link.Start.Y + link.Start.Z + link.End.X + link.End.Y + link.End.Z ) || !(link.Radius > 0) || !float.IsFinite( link.Radius ) || (flags & ~3) != 0 ) return false;
				links[tile].Add( link with { Bidirectional = (flags & 1) != 0, Type = (flags & 2) != 0 ? settings.JumpType : settings.DropType } );
			}
		}
		return reader.BaseStream.Position == length;
	}

	/// <summary>
	/// The relative path to the baked navdata file (without _c suffix)
	/// </summary>
	private string _bakedDataPath;

	internal bool HasBakedDataPath => !string.IsNullOrEmpty( _bakedDataPath );

	/// <summary>
	/// Get the navdata filename for this scene
	/// </summary>
	internal const string BakeFilename = "navmesh/baked.navdata";

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
		jso["GenerateLinks"] = GenerateLinks;
		jso["AgentMaxDrop"] = AgentMaxDrop;
		jso["AgentMaxJumpDistance"] = AgentMaxJumpDistance;
		jso["AgentMaxJumpHeight"] = AgentMaxJumpHeight;
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
		GenerateLinks = (bool)(jso["GenerateLinks"] ?? GenerateLinks);
		AgentMaxDrop = (float)(jso["AgentMaxDrop"] ?? AgentMaxDrop);
		AgentMaxJumpDistance = (float)(jso["AgentMaxJumpDistance"] ?? AgentMaxJumpDistance);
		AgentMaxJumpHeight = (float)(jso["AgentMaxJumpHeight"] ?? AgentMaxJumpHeight);

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
