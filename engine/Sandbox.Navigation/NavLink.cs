using System.Numerics;

namespace Nav;

/// <summary>
/// A hand-placed connection between two points. Radius is the horizontal snap distance at both ends.
/// Two links are equal when every field is.
/// </summary>
public record struct NavLink
{
	public Vector3 Start, End;
	public float Radius;
	public bool Bidirectional;

	/// <summary>
	/// The area id a filter judges the link by, from 1 to NavFilter.MaxArea. NavFilter.NoArea is
	/// refused by AddLink and SetLink.
	/// </summary>
	public byte Area;

	/// <summary>
	/// What kind of link this is, 0 to NavFilter.MaxLinkType, for a filter to forbid or price per
	/// traveller. What a type means is up to the caller.
	/// </summary>
	public byte Type;

	// Made by the mesh from a tile build, not placed by the caller.
	internal bool Generated;

	/// <summary>
	/// A link of area NavFilter.DefaultArea, crossed like plain ground, and type 0.
	/// </summary>
	public static NavLink Create( Vector3 start, Vector3 end, float radius, bool bidirectional )
	{
		return new NavLink { Start = start, End = end, Radius = radius, Bidirectional = bidirectional, Area = NavFilter.DefaultArea, Type = 0 };
	}
}
