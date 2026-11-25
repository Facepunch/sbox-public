namespace Nav;

/// <summary>
/// One agent type of a NavMesh: the settings its tiles are built with and its options. Every field is
/// chosen by the caller.
/// </summary>
public struct NavAgentTypeDesc
{
	public required NavSettings Settings;

	/// <summary>
	/// The links this type's builds generate, the codec and KeepFields of its rebuild cache.
	/// </summary>
	public required NavMeshOptions Options;
}
