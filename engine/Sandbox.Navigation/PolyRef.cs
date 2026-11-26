namespace Nav;

/// <summary>
/// A handle to one polygon or link. It stops resolving when its tile is rebuilt or its link changes.
/// It means something only to the mesh it came from: another mesh, such as one from Load, may resolve it
/// to a polygon of its own.
/// </summary>
public readonly struct PolyRef : IEquatable<PolyRef>
{
	/// <summary>
	/// 0 is none.
	/// </summary>
	public readonly ulong Value;

	// The value is serial:32 | id:32. A polygon's id is its tile slot above the mesh's PolyBits and the
	// polygon below. Ids from LinkIds up name links whatever the split, with the link id in the low 16
	// bits: they are the top slots of every split of at most 16 polygon bits.
	internal const uint LinkIds = 0xFFFF0000;
	// The last id would read as NoId.
	internal const int MaxLinks = 0xFFFF;
	internal const uint NoId = 0xFFFFFFFF;

	internal static int MaxTiles( int polyBits ) => (int)(LinkIds >> polyBits);

	internal PolyRef( uint serial, uint id )
	{
		Value = (ulong)serial << 32 | id;
	}

	internal static PolyRef ForLink( uint serial, int link ) => new( serial, LinkIds | (uint)link );

	public bool IsNone => Value == 0;

	/// <summary>
	/// True when this names a NavLink instead of a polygon.
	/// </summary>
	public bool IsLink => (uint)Value >= LinkIds;

	/// <summary>
	/// The link id, or -1 for a polygon.
	/// </summary>
	public int Link => IsLink ? (int)((uint)Value & 0xFFFF) : -1;

	internal uint Id => (uint)Value;
	internal uint Serial => (uint)(Value >> 32);

	public bool Equals( PolyRef other ) => Value == other.Value;
	public override bool Equals( object? obj ) => obj is PolyRef other && Value == other.Value;
	public override int GetHashCode() => Value.GetHashCode();
	public static bool operator ==( PolyRef a, PolyRef b ) => a.Value == b.Value;
	public static bool operator !=( PolyRef a, PolyRef b ) => a.Value != b.Value;
	public override string ToString() => IsNone ? "none" : IsLink ? $"link {Link}" : $"poly {Id:x8}";
}
