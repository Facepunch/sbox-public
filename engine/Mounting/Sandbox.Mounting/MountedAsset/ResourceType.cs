namespace Sandbox.Mounting;

/// <summary>
/// What resource can come out of this file
/// </summary>
public enum ResourceType
{
	None,
	Text,
	Binary,
	Model,
	Scene,
	Texture,
	Sound,
	Material,

	/// <summary>
	/// Should return a PrefabFile
	/// </summary>
	PrefabFile,
	GameResource
}
