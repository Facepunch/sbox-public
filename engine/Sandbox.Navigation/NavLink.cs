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
}
