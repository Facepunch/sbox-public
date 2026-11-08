namespace Basalt;

/// <summary>
/// Where in a frame a host's camera work runs (<see cref="IRenderStages"/>), in order. The values match s&amp;box's
/// <c>Sandbox.Rendering.Stage</c>.
/// </summary>
public enum RenderStage
{
	/// <summary>After the depth prepass, with depth bound and no colour.</summary>
	AfterDepthPrepass = 1000,

	/// <summary>After opaque geometry.</summary>
	AfterOpaque = 2000,

	/// <summary>After the skybox.</summary>
	AfterSkybox = 3000,

	/// <summary>After translucent geometry.</summary>
	AfterTransparent = 4000,

	/// <summary>After the viewmodel.</summary>
	AfterViewmodel = 5000,

	/// <summary>UI drawn into HDR, before post processing.</summary>
	EarlyUI = 5500,

	/// <summary>Before post processing.</summary>
	BeforePostProcess = 6000,

	/// <summary>Tonemapping.</summary>
	Tonemapping = 6500,

	/// <summary>After post processing, into the final output.</summary>
	AfterPostProcess = 7000,

	/// <summary>Screen UI.</summary>
	UI = 7500,

	/// <summary>After screen UI.</summary>
	AfterUI = 8000,
}

/// <summary>
/// The queue an early stage effect runs on (<see cref="IRenderStages.HasEarlyPostProcess"/>).
/// </summary>
public enum StageQueue
{
	/// <summary>The graphics queue.</summary>
	Graphics,

	/// <summary>The async compute queue.</summary>
	AsyncCompute,
}
