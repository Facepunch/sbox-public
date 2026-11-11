using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sandbox;

/// <summary>
/// Identifies the render pass currently being drawn.
/// </summary>
[Expose]
public enum SceneLayerType
{
	Unknown = 0,

	/// <summary>
	/// Translucent pass. We're rendering translucent objects in depth sorted order, from back to front.
	/// </summary>
	Translucent = 1,

	/// <summary>
	/// Rendering dynamic shadows
	/// </summary>
	Shadow = 4,

	/// <summary>
	/// Translucent effects on the 1/4 texture
	/// </summary>
	EffectsTranslucent = 5,

	/// <summary>
	/// Opaque effects on the 1/4 texture
	/// </summary>
	EffectsOpaque = 6,

	/// <summary>
	/// Depth prepass to reduce overdraw
	/// </summary>
	DepthPrepass = 7,

	Opaque = 8,
}
