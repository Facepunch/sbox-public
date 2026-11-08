namespace Basalt.Shadows;

/// <summary>
/// How shadows are budgeted and filtered (<see cref="RenderHost.Shadows"/>). The defaults are s&amp;box's.
/// </summary>
public class ShadowSettings
{
	/// <summary>Most local lights that cast shadows; the smallest on screen lose theirs first.</summary>
	public virtual int MaxShadows { get; set; } = 256;

	/// <summary>Largest local light shadow map, which is sized by the light's size on screen.</summary>
	public virtual int MaxResolution { get; set; } = 1024;

	/// <summary>Shadow filtering: 0 off, 1 low, 2 medium, 3 high, 4 penumbra.</summary>
	public virtual int Filter { get; set; } = 3;

	/// <summary>Most cascades the sun's shadows fit the view with.</summary>
	public virtual int MaxCascades { get; set; } = 3;

	/// <summary>Each cascade's resolution.</summary>
	public virtual int MaxCascadeResolution { get; set; } = 2048;

	/// <summary>How far from the camera the sun casts shadows.</summary>
	public virtual float CascadeDistance { get; set; } = 15000;

	/// <summary>Whether the sun casts shadows.</summary>
	public virtual bool CascadesEnabled { get; set; } = true;

	/// <summary>Whether spot and point lights cast shadows.</summary>
	public virtual bool LocalEnabled { get; set; } = true;

	/// <summary>Most local light shadow maps re-rendered a frame. New, moved and resized lights always are.</summary>
	public virtual int MaxUpdatesPerFrame { get; set; } = 8;

	/// <summary>Rasterizer depth bias for shadow views; more negative is stronger.</summary>
	public virtual int DepthBias { get; set; } = -1;

	/// <summary>Rasterizer slope scaled depth bias for shadow views; more negative is stronger.</summary>
	public virtual float SlopeScale { get; set; } = -1.5f;

	/// <summary>Screen size, in percent, below which a spot light casts no shadow.</summary>
	public virtual float SizeCullThreshold { get; set; } = 0.25f;
}
