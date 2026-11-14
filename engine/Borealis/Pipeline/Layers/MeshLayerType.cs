namespace Borealis;

/// <summary>
/// What kind of pass a mesh layer is, which decides how objects draw in it. The values match s&amp;box's <c>SceneLayerType</c>.
/// </summary>
public enum MeshLayerType
{
	/// <summary>Not set.</summary>
	Unknown = 0,

	/// <summary>Blended forward drawing.</summary>
	Translucent = 1,

	/// <summary>A shadow map.</summary>
	Shadow = 4,

	/// <summary>Blended effects.</summary>
	EffectsTranslucent = 5,

	/// <summary>Opaque effects.</summary>
	EffectsOpaque = 6,

	/// <summary>The depth prepass.</summary>
	DepthPrepass = 7,

	/// <summary>Opaque forward drawing.</summary>
	Opaque = 8,
}
