namespace Nav;

/// <summary>
/// A handle to one polygon or link. It stops resolving when its tile is rebuilt or its link changes.
/// It means something only to the mesh it came from: every mesh numbers its builds from 1, so another
/// mesh, such as the one a Load returns, may resolve it to a polygon of its own. Keep the mesh with
/// any PolyRef or NavPath that can outlive it.
/// </summary>
public readonly struct PolyRef : IEquatable<PolyRef>
{
	/// <summary>
	/// 0 is none.
	/// </summary>
	public readonly ulong Value;

	// The split of the 32-bit id inside one mesh state: tile slot above, polygon below. The value is
	// serial:32 | slot | poly. The top slot names a link, with the link id in the polygon bits.
	internal const int SlotBits = 18, PolyBits = 14;

	internal const uint PolyMask = (1u << PolyBits) - 1;
	internal const uint LinkSlot = (1u << SlotBits) - 1;
	internal const int MaxTiles = (int)LinkSlot;
	internal const int MaxPolys = (int)PolyMask;
	internal const int MaxLinks = 1 << PolyBits;
	internal const uint NoId = 0xFFFFFFFF;

	internal PolyRef( uint serial, uint id )
	{
		Value = (ulong)serial << 32 | id;
	}

	internal static PolyRef ForLink( uint serial, int link ) => new( serial, LinkSlot << PolyBits | (uint)link );

	public bool IsNone => Value == 0;

	/// <summary>
	/// True when this names a NavLink instead of a polygon.
	/// </summary>
	public bool IsLink => (uint)Value >> PolyBits == LinkSlot;

	/// <summary>
	/// The link id, or -1 for a polygon.
	/// </summary>
	public int Link => IsLink ? (int)((uint)Value & PolyMask) : -1;

	internal uint Id => (uint)Value;
	internal uint Serial => (uint)(Value >> 32);

	public bool Equals( PolyRef other ) => Value == other.Value;
	public override bool Equals( object? obj ) => obj is PolyRef other && Value == other.Value;
	public override int GetHashCode() => Value.GetHashCode();
	public static bool operator ==( PolyRef a, PolyRef b ) => a.Value == b.Value;
	public static bool operator !=( PolyRef a, PolyRef b ) => a.Value != b.Value;
	public override string ToString() => IsNone ? "none" : IsLink ? $"link {Link}" : $"{(uint)Value >> PolyBits}:{(uint)Value & PolyMask}";
}
