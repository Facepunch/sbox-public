namespace Nav;

/// <summary>
/// What a mesh is made with besides its NavSettings, for the constructor and for Load.
/// </summary>
public struct NavMeshOptions
{
	/// <summary>
	/// The links tile builds from triangles generate.
	/// </summary>
	public NavLinkSettings Links;

	/// <summary>
	/// Compression for the rebuild cache. A default NavCodec is none.
	/// </summary>
	public NavCodec Codec;

	/// <summary>
	/// Keep the eroded field of a tile a volume overlaps, so that a volume change rebuilds the tile
	/// without asking for its triangles. False: every volume change asks for them.
	/// </summary>
	public bool KeepFields;

	/// <summary>
	/// Links from NavLinkSettings.Create, so nothing generated, no codec, KeepFields true.
	/// </summary>
	public static NavMeshOptions Create( in NavSettings settings )
	{
		return new NavMeshOptions { Links = NavLinkSettings.Create( settings ), Codec = default, KeepFields = true };
	}
}
