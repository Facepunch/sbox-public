using System.Numerics;

namespace Nav;

public enum VolumeShape : byte
{
	Box,
	Sphere,
	Capsule,
	Everything
}

/// <summary>
/// A box, sphere, capsule or everything-volume that blocks the mesh or marks it with an area id.
/// Two volumes are equal when every field is.
/// </summary>
public record struct NavVolume
{
	public VolumeShape Shape;

	/// <summary>
	/// NavFilter.NoArea blocks, any other id up to NavFilter.MaxArea marks. Where volumes overlap the
	/// higher id wins and NoArea beats all. A higher id than MaxArea is refused when the volume is added.
	/// </summary>
	public byte Area;

	/// <summary>
	/// Any affine transform. The shape test runs in local space.
	/// </summary>
	public Matrix4x4 LocalToWorld;

	/// <summary>
	/// Box: local min and max. Sphere: centre in A. Capsule: the two end points.
	/// </summary>
	public Vector3 A, B;

	/// <summary>
	/// Sphere and capsule radius in local units.
	/// </summary>
	public float Radius;

	/// <summary>
	/// World units the shape grows by when tiles are marked: a box sideways only, a sphere or capsule all round.
	/// At least 0 and finite, else AddVolume and SetVolume throw. A blocker with the agent radius keeps agents
	/// that far from it, as erosion does from a wall.
	/// </summary>
	public float Margin;
}
