using System.Numerics;

namespace Nav;

/// <summary>
/// What NavQuery.FindWalls looks for: walls of the surface connected to Poly within Radius of Center,
/// whose point nearest to Center is no more than Height above or below it. Every field is chosen by
/// the caller.
/// </summary>
public struct WallRequest
{
	public required PolyRef Poly;
	public required Vector3 Center;
	public required float Radius, Height;
}
