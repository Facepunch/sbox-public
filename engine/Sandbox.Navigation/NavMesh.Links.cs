using System.Numerics;
using System.Runtime.InteropServices;
using Nav.Build;

namespace Nav;

public sealed partial class NavMesh
{
	// Volumes by id, shared by every agent type: each type keeps the tiles a volume reaches in its grid.
	internal NavVolume[] VolumeDefs = [];
	internal ulong[] VolumeHashes = [];
	internal bool[] VolumeUsed = [];
	internal int VolumeHigh;
	internal int VolumeCapacity => VolumeDefs.Length;

	// Links by id, shared by every agent type: each type resolves them on its own tiles. With several
	// types, linkScopes says per id which have the link: 0 every type, else the one type numbered one
	// less. A mesh of one type has none.
	internal NavLink[] LinkDefs = [];
	internal int LinkCapacity => LinkDefs.Length;
	byte[]? linkScopes;
	int linkHigh;
	// Ids below linkHigh that are free or removed, lowest first.
	readonly PriorityQueue<int, int> freeLinks = new();

	/// <summary>
	/// Adds a volume and marks the tiles of every agent type it reaches dirty. They rebuild from their
	/// kept field, or ask for geometry once when they have none. Returns the id, the lowest one that is
	/// free. Throws for an area id above NavFilter.MaxArea. On a loaded mesh, add the volumes of the
	/// scene before the first TakeJobs: a tile baked with volumes that are not there by then is rebuilt.
	/// </summary>
	public int AddVolume( in NavVolume volume )
	{
		CheckArea( volume );
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
	/// is not in use and for an area id above NavFilter.MaxArea.
	/// </summary>
	public void SetVolume( int id, in NavVolume volume )
	{
		if ( (uint)id >= (uint)VolumeHigh || !VolumeUsed[id] ) throw new ArgumentException( "No such volume", nameof( id ) );
		CheckArea( volume );
		Place( id, volume, true );
	}

	public void RemoveVolume( int id )
	{
		if ( (uint)id >= (uint)VolumeHigh || !VolumeUsed[id] ) return;
		VolumeUsed[id] = false;
		foreach ( var type in types ) type.RemoveVolume( id );
	}

	// A filter has one bit per area id, and a bake refuses a polygon with a higher one.
	static void CheckArea( in NavVolume volume )
	{
		if ( volume.Area > NavFilter.MaxArea ) throw new ArgumentOutOfRangeException( nameof( volume ), $"Volume area {volume.Area} is above {NavFilter.MaxArea}" );
	}

	void Place( int id, in NavVolume volume, bool replaced )
	{
		VolumeDefs[id] = volume;
		VolumeHashes[id] = Hash( volume );
		foreach ( var type in types ) type.PlaceVolume( id, volume, replaced );
	}

	// Over the fields of the volume, the same in every process because the hash is kept in bakes.
	// Never 0, which stands for no volumes.
	static ulong Hash( in NavVolume volume )
	{
		Span<byte> bytes = stackalloc byte[2 + 23 * 4];
		bytes[0] = (byte)volume.Shape;
		bytes[1] = volume.Area;
		var m = volume.LocalToWorld;
		ReadOnlySpan<float> fields =
		[
			m.M11, m.M12, m.M13, m.M14, m.M21, m.M22, m.M23, m.M24, m.M31, m.M32, m.M33, m.M34, m.M41, m.M42, m.M43, m.M44,
			volume.A.X, volume.A.Y, volume.A.Z, volume.B.X, volume.B.Y, volume.B.Z, volume.Radius
		];
		MemoryMarshal.AsBytes( fields ).CopyTo( bytes.Slice( 2 ) );
		return Fnv.Hash( bytes );
	}

	/// <summary>
	/// A link of every agent type: each resolves it on its own tiles, and a type where an end finds no
	/// polygon within the link's radius has it unconnected. Takes effect at the next Commit. Returns the
	/// link id, the lowest that is free, or -1 when all 65,535 are in use, generated links of every type
	/// included. Throws for an area id of NavFilter.NoArea or above NavFilter.MaxArea and a type above
	/// NavFilter.MaxLinkType.
	/// </summary>
	public int AddLink( in NavLink link ) => NewLink( Checked( link ), -1 );

	/// <summary>
	/// A link of one agent type only, such as a generated link of that type that the caller adds back.
	/// Otherwise as AddLink. Throws for a type the mesh does not have.
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

	// The types that have the link: every one, or one.
	void Pend( int id, byte status )
	{
		int scope = linkScopes?[id] ?? 0;
		if ( scope != 0 ) types[scope - 1].Pend( id, status );
		else foreach ( var type in types ) type.Pend( id, status );
	}

	bool LinkInUse( int id ) => (uint)id < (uint)linkHigh && types[Math.Max( (linkScopes?[id] ?? 0) - 1, 0 )].LinkInUse( id );

	// An area id or type out of range is refused: a filter has one bit per id, and no link is NoArea.
	static NavLink Checked( in NavLink link )
	{
		if ( link.Area == NavFilter.NoArea ) throw new ArgumentOutOfRangeException( nameof( link ), "Link area 0 is NavFilter.NoArea, which no link can have: use NavLink.Create or set an area from 1" );
		if ( link.Area > NavFilter.MaxArea ) throw new ArgumentOutOfRangeException( nameof( link ), $"Link area {link.Area} is above {NavFilter.MaxArea}" );
		if ( link.Type > NavFilter.MaxLinkType ) throw new ArgumentOutOfRangeException( nameof( link ), $"Link type {link.Type} is above {NavFilter.MaxLinkType}" );
		return link;
	}

	/// <summary>
	/// Throws for an id that is not in use, for a generated link, and for an area id of NavFilter.NoArea
	/// or above NavFilter.MaxArea or a type above NavFilter.MaxLinkType, as AddLink does. The link keeps
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
	/// Appends the generated links of a tile that are good as of the last Commit, and returns how many:
	/// both ends on the mesh, clear of blocking volumes, not as short to walk, thinned. Always in the
	/// same order, and the same values for the same mesh, so a caller that keeps them compares the old
	/// set with the new by value. Each has its ends on the polygons it joins and a radius of a cell:
	/// added with AddLink it connects the same polygons; on a mesh of several agent types add it to its
	/// own type with AddLink( agentType, link ). Owner thread.
	/// </summary>
	public int GetGeneratedLinks( int tileX, int tileY, List<NavLink> result ) => Single.GetGeneratedLinks( tileX, tileY, result );
	public int GetGeneratedLinks( int agentType, int tileX, int tileY, List<NavLink> result ) => TypeAt( agentType ).GetGeneratedLinks( tileX, tileY, result );

	/// <summary>
	/// Appends the tiles whose generated links changed in a Commit since the last call, and returns how
	/// many: a tile built or removed, one whose links across a border were judged, one a blocking volume
	/// came over or left. A tile built again with the same links is not among them. Kept only for a mesh
	/// that does not insert its links, whose caller has to ask. Owner thread.
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
