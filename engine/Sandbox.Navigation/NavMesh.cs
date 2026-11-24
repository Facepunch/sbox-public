using System.Numerics;
using System.Runtime.InteropServices;
using Nav.Build;

namespace Nav;

/// <summary>
/// The tiles and links of one navigation world for one or more agent types, the volumes that mark
/// areas on it, and the rebuild cache: the eroded field of every tile a volume overlaps, kept so that a
/// volume change rebuilds the tile without its triangles. One owner thread changes it, any number of
/// threads query it without locks: every Commit swaps in new immutable states. Everything that marks,
/// takes, commits, adds, sets or removes belongs on the owner thread.
/// Each agent type has settings, tiles, jobs and generated links of its own; the types share the
/// geometry, the volumes, the links placed by the caller, the commit and the bake. A call that is about
/// one type's tiles or links has a form that names the type; its form without one is for a mesh of one
/// agent type and throws on a mesh of several.
/// Setting a link or volume id that is not in use throws; removing one does nothing.
/// </summary>
public sealed partial class NavMesh
{
	/// <summary>
	/// The most agent types one mesh has.
	/// </summary>
	public const int MaxAgentTypes = 16;

	readonly MeshAgentType[] types;

	/// <summary>
	/// A mesh of one agent type. Throws when the settings or the link settings of the options are not
	/// valid. With links enabled, tile builds from triangles also find drops and jumps.
	/// </summary>
	public NavMesh( in NavSettings settings, in NavMeshOptions options )
	{
		Check( settings, options, -1 );
		types = [new MeshAgentType( this, 0, settings, options )];
		maxRound = uint.MaxValue;
	}

	/// <summary>
	/// A mesh of 1 to MaxAgentTypes agent types, numbered in the order given. Throws for none, more, or a
	/// type whose settings or link settings are not valid. The types' settings are independent of each
	/// other: cell sizes, tile sizes and polygon limits may all differ.
	/// </summary>
	public NavMesh( ReadOnlySpan<NavAgentTypeDesc> agentTypes )
	{
		if ( agentTypes.Length < 1 || agentTypes.Length > MaxAgentTypes ) throw new ArgumentException( $"A mesh has 1 to {MaxAgentTypes} agent types, not {agentTypes.Length}", nameof( agentTypes ) );
		for ( int i = 0; i < agentTypes.Length; i++ ) Check( agentTypes[i].Settings, agentTypes[i].Options, i );
		types = new MeshAgentType[agentTypes.Length];
		for ( int i = 0; i < types.Length; i++ ) types[i] = new MeshAgentType( this, i, agentTypes[i].Settings, agentTypes[i].Options );
		maxRound = (uint.MaxValue - (uint)(types.Length - 1)) / (uint)types.Length;
		if ( types.Length > 1 ) linkScopes = [];
	}

	static void Check( in NavSettings settings, in NavMeshOptions options, int type )
	{
		string prefix = type < 0 ? "" : $"Agent type {type}: ";
		if ( !settings.IsValid( out var reason ) ) throw new ArgumentException( prefix + reason, type < 0 ? nameof( settings ) : "agentTypes" );
		if ( !options.Links.IsValid( settings, out reason ) ) throw new ArgumentException( prefix + reason, type < 0 ? nameof( options ) : "agentTypes" );
	}

	public int AgentTypeCount => types.Length;

	/// <summary>
	/// What the agent type was made with. Throws for a type the mesh does not have.
	/// </summary>
	public NavAgentTypeDesc GetAgentType( int agentType )
	{
		var type = TypeAt( agentType );
		return new NavAgentTypeDesc { Settings = type.Settings, Options = type.Options };
	}

	internal MeshAgentType TypeAt( int agentType )
	{
		if ( (uint)agentType >= (uint)types.Length ) throw new ArgumentOutOfRangeException( nameof( agentType ), $"Agent type {agentType} is not one of the mesh's {types.Length}" );
		return types[agentType];
	}

	// The one type of a mesh of one, for the calls that do not name a type.
	internal MeshAgentType Single => types.Length == 1 ? types[0] : throw new InvalidOperationException( $"The mesh has {types.Length} agent types: name the agent type" );

	internal ReadOnlySpan<MeshAgentType> Types => types;

	/// <summary>
	/// The settings of a mesh of one agent type. See GetAgentType.
	/// </summary>
	public NavSettings Settings => Single.Settings;
	public NavLinkSettings LinkSettings => Single.LinkSettings;

	/// <summary>
	/// Bumped once by every Commit that changed the mesh, whichever agent types it changed.
	/// </summary>
	public uint Version => types.Length == 1 ? types[0].State.Version : Volatile.Read( ref version );

	uint version;

	/// <summary>
	/// Tile coordinates are unbounded and may be negative.
	/// </summary>
	public (int X, int Y) GetTileAt( Vector3 point ) => Single.GetTileAt( point );
	public (int X, int Y) GetTileAt( int agentType, Vector3 point ) => TypeAt( agentType ).GetTileAt( point );

	/// <summary>
	/// RuntimeBytes is what queries need, including the island data of one area mask. The triangle
	/// counters are running totals over every build since the mesh was made, rebuilds included, not the
	/// triangles of the tiles there are now. The sums over every agent type. Safe on any thread.
	/// </summary>
	public (int Tiles, int Polygons, int Vertices, long RuntimeBytes, long CacheBytes, long TrianglesUsed, long TrianglesRejected) GetStats()
	{
		if ( types.Length == 1 ) return types[0].GetStats();
		(int Tiles, int Polygons, int Vertices, long RuntimeBytes, long CacheBytes, long TrianglesUsed, long TrianglesRejected) sum = default;
		foreach ( var type in types )
		{
			var s = type.GetStats();
			sum = (sum.Tiles + s.Tiles, sum.Polygons + s.Polygons, sum.Vertices + s.Vertices, sum.RuntimeBytes + s.RuntimeBytes, sum.CacheBytes + s.CacheBytes, sum.TrianglesUsed + s.TrianglesUsed, sum.TrianglesRejected + s.TrianglesRejected);
		}
		return sum;
	}

	/// <summary>
	/// The stats of one agent type, as GetStats() has them for the whole mesh.
	/// </summary>
	public (int Tiles, int Polygons, int Vertices, long RuntimeBytes, long CacheBytes, long TrianglesUsed, long TrianglesRejected) GetStats( int agentType ) => TypeAt( agentType ).GetStats();

	/// <summary>
	/// Writes the coordinates of loaded tiles and returns how many were written.
	/// </summary>
	public int GetTiles( Span<(int X, int Y)> coordinates ) => Single.GetTiles( coordinates );
	public int GetTiles( int agentType, Span<(int X, int Y)> coordinates ) => TypeAt( agentType ).GetTiles( coordinates );

	/// <summary>
	/// Appends the polygons of one tile, counter-clockwise seen from above: for consecutive vertices
	/// a, b, c the cross product of b - a and c - b points up.
	/// </summary>
	public void GetPolygons( int tileX, int tileY, List<Vector3> vertices, List<int> vertexCounts, List<byte> areas ) => Single.GetPolygons( tileX, tileY, vertices, vertexCounts, areas );
	public void GetPolygons( int agentType, int tileX, int tileY, List<Vector3> vertices, List<int> vertexCounts, List<byte> areas ) => TypeAt( agentType ).GetPolygons( tileX, tileY, vertices, vertexCounts, areas );

	// For the tests and the lab, which reach internals of a mesh of one type.
	internal MeshState State => Single.State;
	internal long HeldBytes => Single.HeldBytes;
	internal long HeldPeak => Single.HeldPeak;
	internal int HeldFields => Single.HeldFields;
	internal int MaxHeldFields { get => Single.MaxHeldFields; set => Single.MaxHeldFields = value; }
	internal void SkipSlots( int slot ) => Single.slotCount = slot;
	internal int MaxTiles => Single.MaxTiles;
	internal ulong VolumeHashOf( int tileX, int tileY ) => Single.VolumeHashOf( tileX, tileY );
	internal void SetVolumeHashes( ReadOnlySpan<Tile> tiles, ReadOnlySpan<ulong> hashes ) => Single.SetVolumeHashes( tiles, hashes );
	internal int CommitTiles( ReadOnlySpan<Tile> tiles )
	{
		counted = false;
		return Single.CommitTiles( tiles );
	}
}
