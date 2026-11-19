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
	/// The area id a filter judges the link by, up to NavFilter.MaxArea. 0, the default, stands for
	/// NavFilter.DefaultArea, so a link that sets nothing is crossed like plain ground.
	/// </summary>
	public byte Area;

	/// <summary>
	/// What kind of link this is, 0 to NavFilter.MaxLinkType, for a filter to forbid or price per
	/// traveller. What a type means is up to the caller.
	/// </summary>
	public byte Type;

	// Made by the mesh from a tile build, not placed by the caller.
	internal bool Generated;
}
