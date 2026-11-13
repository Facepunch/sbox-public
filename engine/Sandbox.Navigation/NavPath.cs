using System.Numerics;

namespace Nav;

public enum PathStatus { StartNotFound, EndNotFound, NotFound, Partial, Complete }

/// <summary>
/// A reusable path result. Keep one and pass it to every FindPath: its buffers only grow.
/// </summary>
public sealed class NavPath
{
	internal PolyRef[] polys = new PolyRef[32];
	internal Vector3[] corners = new Vector3[16];
	internal int polyCount, cornerCount;

	/// <summary>
	/// What the last search into this path found. A path no search has filled reports StartNotFound and
	/// is empty.
	/// </summary>
	public PathStatus Status { get; internal set; }

	public bool IsValid => Status >= PathStatus.Partial;

	/// <summary>
	/// The polygon corridor. A link entry sits between the two polygons it joins.
	/// </summary>
	public ReadOnlySpan<PolyRef> Polys => polys.AsSpan( 0, polyCount );

	/// <summary>
	/// String-pulled points from the snapped start to the snapped end. A link the path takes gives two
	/// corners in a row, its entry and its exit; Polys has the link between its two polygons, and
	/// NavQuery.GetLink returns the same two points when it is asked from the corner before the link.
	/// For a link that goes both ways GetLink answers by where it is asked from, not by the corridor.
	/// </summary>
	public ReadOnlySpan<Vector3> Corners => corners.AsSpan( 0, cornerCount );

	/// <summary>
	/// Snapped ends. End is the reachable end when the path is partial.
	/// </summary>
	public Vector3 Start { get; internal set; }
	public Vector3 End { get; internal set; }

	public float Length { get; internal set; }

	public void CopyFrom( NavPath other )
	{
		if ( polys.Length < other.polyCount ) polys = new PolyRef[other.polys.Length];
		if ( corners.Length < other.cornerCount ) corners = new Vector3[other.corners.Length];
		other.Polys.CopyTo( polys );
		other.Corners.CopyTo( corners );
		polyCount = other.polyCount;
		cornerCount = other.cornerCount;
		Status = other.Status;
		Start = other.Start;
		End = other.End;
		Length = other.Length;
	}

	internal void Clear( PathStatus status )
	{
		Status = status;
		polyCount = 0;
		cornerCount = 0;
		Length = 0;
	}
}
