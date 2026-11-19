using NativeEngine;

namespace Sandbox;

/// <summary>
/// Keep this internal for now
/// </summary>
internal class SceneLightProbe : SceneObject
{
	private CSceneLightProbeVolumeObject lightProbeVolume;

	/// <summary>A probe volume lights its world's objects inside it; it isn't drawn.</summary>
	internal override bool LightsWorld => true;

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

		probeTexture = texture;
		this.indicesTexture = indicesTexture;
		this.scalarsTexture = scalarsTexture;

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

	readonly Texture probeTexture, indicesTexture, scalarsTexture;

	/// <summary>
	/// The volume's probe texture and its direct light indices and scalars, as it was made with them, or native's for a volume
	/// native made (the map a native tool loads for its preview).
	/// </summary>
	internal Texture ProbeTexture => probeTexture ?? (lightProbeVolume.IsValid ? Texture.FromNativeResource( lightProbeVolume.m_hLightProbeTexture ) : null);
	internal Texture IndicesTexture => indicesTexture ?? (lightProbeVolume.IsValid ? Texture.FromNativeResource( lightProbeVolume.m_hLightProbeDirectLightIndicesTexture ) : null);
	internal Texture ScalarsTexture => scalarsTexture ?? (lightProbeVolume.IsValid ? Texture.FromNativeResource( lightProbeVolume.m_hLightProbeDirectLightScalarsTexture ) : null);

	/// <summary>The handshake objects the map compiler bound to this volume carry (<c>m_nHandshake</c>).</summary>
	internal int Handshake => lightProbeVolume.IsValid ? lightProbeVolume.m_nHandshake : 0;

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
