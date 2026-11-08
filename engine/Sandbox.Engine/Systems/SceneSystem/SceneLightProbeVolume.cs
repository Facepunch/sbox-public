using NativeEngine;

namespace Sandbox;

/// <summary>
/// Keep this internal for now
/// </summary>
internal class SceneLightProbe : SceneObject
{
	private CSceneLightProbeVolumeObject lightProbeVolume;

	internal SceneLightProbe( HandleCreationData d ) : base( d )
	{
	}

	internal SceneLightProbe( SceneWorld sceneWorld,
		Texture texture,
		Texture indicesTexture,
		Texture scalarsTexture,
		BBox bounds,
		Transform transform,
		int handshake,
		int renderPriority ) : base()
	{
		Assert.IsValid( sceneWorld );

		using ( var h = IHandle.MakeNextHandle( this ) )
		{
			CSceneSystem.CreateLightProbeVolume( sceneWorld );
		}

		lightProbeVolume.m_vBoxMins = bounds.Mins;
		lightProbeVolume.m_vBoxMaxs = bounds.Maxs;
		lightProbeVolume.m_nHandshake = handshake;
		lightProbeVolume.m_nRenderPriority = renderPriority;

		if ( texture != null )
			lightProbeVolume.m_hLightProbeTexture = texture.native;

		if ( indicesTexture != null )
			lightProbeVolume.m_hLightProbeDirectLightIndicesTexture = indicesTexture.native;

		if ( scalarsTexture != null )
			lightProbeVolume.m_hLightProbeDirectLightScalarsTexture = scalarsTexture.native;

		LocalBounds = bounds;
		Transform = transform;

		RenderingEnabled = true;

		lightProbeVolume.CreateConstants();

		CSceneSystem.MarkLightProbeVolumeObjectUpdated( this );
	}

	internal override void OnNativeInit( CSceneObject ptr )
	{
		base.OnNativeInit( ptr );

		lightProbeVolume = (CSceneLightProbeVolumeObject)ptr;
	}

	internal override void OnNativeDestroy()
	{
		lightProbeVolume = default;
		base.OnNativeDestroy();
	}
}
