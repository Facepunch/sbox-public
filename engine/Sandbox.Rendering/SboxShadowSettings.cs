namespace Sandbox.Rendering;

/// <summary>
/// Basalt's shadow settings from the engine's <c>r.shadows</c> convars.
/// </summary>
internal sealed class SboxShadowSettings : Basalt.Shadows.ShadowSettings
{
	public override int MaxShadows { get => ShadowMapper.MaxShadows; set => ShadowMapper.MaxShadows = value; }
	public override int MaxResolution { get => ShadowMapper.MaxResolution; set => ShadowMapper.MaxResolution = value; }
	public override int Filter { get => ShadowMapper.ShadowFilter; set => ShadowMapper.ShadowFilter = value; }
	public override int MaxCascades { get => ShadowMapper.MaxCascades; set => ShadowMapper.MaxCascades = value; }
	public override int MaxCascadeResolution { get => ShadowMapper.MaxCascadeResolution; set => ShadowMapper.MaxCascadeResolution = value; }
	public override float CascadeDistance { get => ShadowMapper.CascadeDistance; set => ShadowMapper.CascadeDistance = value; }
	public override bool CascadesEnabled { get => ShadowMapper.CSMEnabled; set => ShadowMapper.CSMEnabled = value; }
	public override bool LocalEnabled { get => ShadowMapper.LocalShadowsEnabled; set => ShadowMapper.LocalShadowsEnabled = value; }
	public override int MaxUpdatesPerFrame { get => ShadowMapper.MaxUpdatesPerFrame; set => ShadowMapper.MaxUpdatesPerFrame = value; }
	public override int DepthBias { get => ShadowMapper.ShadowDepthBias; set => ShadowMapper.ShadowDepthBias = value; }
	public override float SlopeScale { get => ShadowMapper.ShadowSlopeScale; set => ShadowMapper.ShadowSlopeScale = value; }
	public override float SizeCullThreshold { get => ShadowMapper.SizeCullThreshold; set => ShadowMapper.SizeCullThreshold = value; }
}
