using System.Numerics;

namespace Nav;

/// <summary>
/// How a corridor handed to NavCrowd.SetPath ends: End is where it leads on the mesh, Target what was
/// asked for, Partial whether End stops short of Target. Every field is chosen by the caller.
/// </summary>
public struct RouteDesc
{
	public required Vector3 End, Target;
	public required bool Partial;
}
