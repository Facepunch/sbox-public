using Sandbox.Volumes;

namespace Sandbox;

/// <summary>
/// Owned post-process volume whose point containment follows its water body's rendered surface.
/// Native effect collection evaluates this boundary separately for each camera.
/// </summary>
[Hide]
internal sealed class WaterPostProcessVolume : PostProcessVolume, VolumeSystem.IVolume
{
	/// <summary>
	/// Water body supplying this helper's bounds and appearance.
	/// </summary>
	internal WaterBody Body { get; set; }

	/// <summary>
	/// Only the winning containing water contributes, so overlapping water looks never stack.
	/// </summary>
	bool VolumeSystem.IVolume.Test( Vector3 worldPosition )
	{
		return Body.IsValid() && Body.Underwater && Body.ContainsViewBounds( worldPosition )
			&& WaterBody.FindUnderwaterView( Scene, worldPosition, Body ) == Body;
	}
}
