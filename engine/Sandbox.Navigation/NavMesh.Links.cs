using System.Numerics;
using System.Runtime.InteropServices;
using Nav.Build;

namespace Nav;

public sealed partial class NavMesh
{
	// Volumes by id, shared by every agent type.
	internal NavVolume[] VolumeDefs = [];
	internal ulong[] VolumeHashes = [];
	internal bool[] VolumeUsed = [];
	internal int VolumeHigh;
	internal int VolumeCapacity => VolumeDefs.Length;

	// Links by id, shared by every agent type. linkScopes, with several types only: 0 every type, else type + 1.
	internal NavLink[] LinkDefs = [];
	internal int LinkCapacity => LinkDefs.Length;
	byte[]? linkScopes;
	int linkHigh;
	// Ids below linkHigh that are free or removed, lowest first.
	readonly PriorityQueue<int, int> freeLinks = new();

	/// <summary>
	/// Adds a volume and marks the tiles it reaches dirty in every agent type; they rebuild from their kept
	/// field or ask for geometry. Returns the lowest free id. Throws for an area id above NavFilter.MaxArea and
	/// a margin that is negative or not finite.
	/// On a loaded mesh, add the scene's volumes before the first TakeJobs, or tiles baked with them rebuild.
	/// </summary>
	public int AddVolume( in NavVolume volume )
	{
		Check( volume );
		int id = 0;
		while ( id < VolumeHigh && VolumeUsed[id] ) id++;
		if ( id == VolumeDefs.Length )
		{
			int capacity = Math.Max( 16, id * 2 );
			Array.Resize( ref VolumeDefs, capacity );
			Array.Resize( ref VolumeHashes, capacity );
			Array.Resize( ref VolumeUsed, capacity );
		}
		VolumeHigh = Math.Max( VolumeHigh, id + 1 );
		VolumeUsed[id] = true;
		Place( id, volume, false );
		return id;
	}

	/// <summary>
	/// Replaces a volume. The tiles of the old and the new place are marked dirty. Throws for an id that
	/// is not in use, for an area id above NavFilter.MaxArea and a margin that is negative or not finite.
	/// </summary>
	public void SetVolume( int id, in NavVolume volume )
	{
		if ( (uint)id >= (uint)VolumeHigh || !VolumeUsed[id] ) throw new ArgumentException( "No such volume", nameof( id ) );
		Check( volume );
		Place( id, volume, true );
	}

	public void RemoveVolume( int id )
	{
		if ( (uint)id >= (uint)VolumeHigh || !VolumeUsed[id] ) return;
		VolumeUsed[id] = false;
		foreach ( var type in types ) type.RemoveVolume( id );
	}

	// A filter has one bit per area id, and a bake refuses a polygon with a higher one.
	static void Check( in NavVolume volume )
	{
		if ( volume.Area > NavFilter.MaxArea ) throw new ArgumentOutOfRangeException( nameof( volume ), $"Volume area {volume.Area} is above {NavFilter.MaxArea}" );
		if ( !(volume.Margin >= 0) || !float.IsFinite( volume.Margin ) ) throw new ArgumentOutOfRangeException( nameof( volume ), $"Volume margin {volume.Margin} is not a finite number of at least 0" );
	}

	void Place( int id, in NavVolume volume, bool replaced )
	{
		VolumeDefs[id] = volume;
		VolumeHashes[id] = Hash( volume );
		foreach ( var type in types ) type.PlaceVolume( id, volume, replaced );
	}

	// Stable across processes since bakes keep it; never 0, which means no volumes. The margin is last, where it
	// was before decision 77, so a margin of 0 hashes as it did then and as it did while the field was gone.
	static ulong Hash( in NavVolume volume )
	{
		Span<byte> bytes = stackalloc byte[2 + 24 * 4];
		bytes[0] = (byte)volume.Shape;
		bytes[1] = volume.Area;
		var m = volume.LocalToWorld;
		ReadOnlySpan<float> fields =
		[
			m.M11, m.M12, m.M13, m.M14, m.M21, m.M22, m.M23, m.M24, m.M31, m.M32, m.M33, m.M34, m.M41, m.M42, m.M43, m.M44,
			volume.A.X, volume.A.Y, volume.A.Z, volume.B.X, volume.B.Y, volume.B.Z, volume.Radius, volume.Margin
		];
		MemoryMarshal.AsBytes( fields ).CopyTo( bytes.Slice( 2 ) );
		return Fnv.Hash( bytes );
	}

	/// <summary>
	/// A link of every agent type; a type where an end finds no polygon within the link's radius has it
	/// unconnected. Takes effect at the next Commit. Returns the lowest free id, or -1 when all 65,535 are
	/// in use, generated links included. Throws for an area of NavFilter.NoArea or above NavFilter.MaxArea
	/// and a type above NavFilter.MaxLinkType.
	/// </summary>
	public int AddLink( in NavLink link ) => NewLink( Checked( link ), -1 );

	/// <summary>
	/// AddLink for one agent type only, such as a generated link the caller adds back. Throws for a type
	/// the mesh does not have.
	/// </summary>
	public int AddLink( int agentType, in NavLink link ) => NewLink( Checked( link ), TypeAt( agentType ).Index );

	int NewLink( in NavLink value, int type )
	{
		if ( !freeLinks.TryDequeue( out int id, out _ ) )
		{
			if ( linkHigh == PolyRef.MaxLinks ) return -1;
			id = linkHigh++;
		}
		if ( id == LinkDefs.Length )
		{
			int capacity = Math.Min( PolyRef.MaxLinks, Math.Max( 16, LinkDefs.Length * 2 ) );
			Array.Resize( ref LinkDefs, capacity );
			if ( linkScopes != null ) Array.Resize( ref linkScopes, capacity );
		}
		LinkDefs[id] = value;
		if ( linkScopes != null ) linkScopes[id] = (byte)(type + 1);
		Pend( id, MeshAgentType.LinkDirty );
		return id;
	}

	void Pend( int id, byte status )
	{
		int scope = linkScopes?[id] ?? 0;
		if ( scope != 0 ) types[scope - 1].Pend( id, status );
		else foreach ( var type in types ) type.Pend( id, status );
	}

	bool LinkInUse( int id ) => (uint)id < (uint)linkHigh && types[Math.Max( (linkScopes?[id] ?? 0) - 1, 0 )].LinkInUse( id );

	static NavLink Checked( in NavLink link )
	{
		if ( link.Area == NavFilter.NoArea ) throw new ArgumentOutOfRangeException( nameof( link ), "Link area 0 is NavFilter.NoArea, which no link can have: use NavLink.Create or set an area from 1" );
		if ( link.Area > NavFilter.MaxArea ) throw new ArgumentOutOfRangeException( nameof( link ), $"Link area {link.Area} is above {NavFilter.MaxArea}" );
		if ( link.Type > NavFilter.MaxLinkType ) throw new ArgumentOutOfRangeException( nameof( link ), $"Link type {link.Type} is above {NavFilter.MaxLinkType}" );
		return link;
	}

	/// <summary>
	/// Throws for an id that is not in use, a generated link, and what AddLink throws for. The link keeps
	/// the agent types it was added to.
	/// </summary>
	public void SetLink( int id, in NavLink link )
	{
		if ( !LinkInUse( id ) ) throw new ArgumentException( "No such link", nameof( id ) );
		if ( LinkDefs[id].Generated ) throw new ArgumentException( "The link is a generated one", nameof( id ) );
		LinkDefs[id] = Checked( link );
		Pend( id, MeshAgentType.LinkDirty );
	}

	/// <summary>
	/// Does nothing for an id that is not in use and for a generated link: those go with their tile.
	/// </summary>
	public void RemoveLink( int id )
	{
		if ( (uint)id < (uint)linkHigh && !LinkDefs[id].Generated ) FreeLink( id );
	}

	internal void FreeLink( int id )
	{
		if ( !LinkInUse( id ) ) return;
		Pend( id, MeshAgentType.LinkRemoved );
		freeLinks.Enqueue( id, id );
	}

	/// <summary>
	/// The ends of a link as of the last Commit: snapped where an end found the mesh, as placed otherwise.
	/// </summary>
	public bool GetLinkEnds( int id, out Vector3 start, out Vector3 end, out bool startConnected, out bool endConnected ) => Single.GetLinkEnds( id, out start, out end, out startConnected, out endConnected );

	/// <summary>
	/// The ends of a link in one agent type as of the last Commit. False for a link that type does not have.
	/// </summary>
	public bool GetLinkEnds( int agentType, int id, out Vector3 start, out Vector3 end, out bool startConnected, out bool endConnected ) => TypeAt( agentType ).GetLinkEnds( id, out start, out end, out startConnected, out endConnected );

	/// <summary>
	/// The link as it was added or generated, as of the last Commit. False for an id that is not in use.
	/// </summary>
	public bool GetLink( int id, out NavLink link, out bool generated ) => Single.GetLink( id, out link, out generated );

	/// <summary>
	/// GetLink for one agent type: false for a link that type does not have.
	/// </summary>
	public bool GetLink( int agentType, int id, out NavLink link, out bool generated ) => TypeAt( agentType ).GetLink( id, out link, out generated );

	/// <summary>
	/// Appends the ids of the generated links, as of the last Commit, and returns how many.
	/// </summary>
	public int GetGeneratedLinks( List<int> ids ) => Single.GetGeneratedLinks( ids );
	public int GetGeneratedLinks( int agentType, List<int> ids ) => TypeAt( agentType ).GetGeneratedLinks( ids );

	/// <summary>
	/// Appends the generated links of a tile that are good as of the last Commit (both ends on the mesh,
	/// clear of blocking volumes, not as short to walk, thinned) and returns how many. Same order and values
	/// for the same mesh, so a caller can compare sets by value. Each has a radius of a cell and, added with
	/// AddLink (to its own type on a mesh of several), connects the same polygons. Owner thread.
	/// </summary>
	public int GetGeneratedLinks( int tileX, int tileY, List<NavLink> result ) => Single.GetGeneratedLinks( tileX, tileY, result );
	public int GetGeneratedLinks( int agentType, int tileX, int tileY, List<NavLink> result ) => TypeAt( agentType ).GetGeneratedLinks( tileX, tileY, result );

	/// <summary>
	/// Appends the tiles whose generated links changed in a Commit since the last call, and returns how
	/// many. A tile rebuilt with the same links is not among them. Kept only for a mesh that does not insert
	/// its links. Owner thread.
	/// </summary>
	public int TakeLinkChanges( List<(int X, int Y)> tiles ) => Single.TakeLinkChanges( tiles );
	public int TakeLinkChanges( int agentType, List<(int X, int Y)> tiles ) => TypeAt( agentType ).TakeLinkChanges( tiles );

	/// <summary>
	/// Generated links of every agent type that are not in the mesh because no link id was free. Owner thread.
	/// </summary>
	public int LinksRefused
	{
		get
		{
			int count = 0;
			foreach ( var type in types ) count += type.LinksRefused;
			return count;
		}
	}
}
