using NativeEngine;

namespace Sandbox.Rendering;

/// <summary>
/// <see cref="ContactShadows"/> for native's views. Called after Hi-Z (depth chain ready); bindless index set in CSM setup.
/// </summary>
internal partial class ShadowMapper
{
	internal void RenderScreenSpaceShadows( ISceneView view )
	{
		if ( SceneView != view || DirectionalLight is null )
			return;

		var light = DirectionalLight;
		if ( !ContactShadows.Enabled || !light.IsValid() || !light.ContactShadows )
			return;

		var mask = light.GetShadowMask( view );
		if ( mask is null )
			return;

		// Directional WorldDirection is already -Forward (CSceneLightObject::SetWorldDirection).
		ContactShadows.Render( mask, view.GetFrustum().GetReverseZViewProjTranspose(), light.WorldDirection, light.ShadowHardness );
	}
}
