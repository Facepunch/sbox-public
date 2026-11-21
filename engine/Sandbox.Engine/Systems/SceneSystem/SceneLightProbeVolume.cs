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
		made = (bounds.Mins, bounds.Maxs, handshake, renderPriority);

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

	// What managed code made it with; null for a volume native made (Hammer's), whose settings native keeps
	readonly (Vector3 Mins, Vector3 Maxs, int Handshake, int Priority)? made;

	/// <summary>
	/// The volume's probe texture and its direct light indices and scalars, as it was made with them, or native's for a volume
	/// native made (the map a native tool loads for its preview).
	/// </summary>
	internal Texture ProbeTexture => probeTexture ?? (lightProbeVolume.IsValid ? Texture.FromNativeResource( lightProbeVolume.m_hLightProbeTexture ) : null);
	internal Texture IndicesTexture => indicesTexture ?? (lightProbeVolume.IsValid ? Texture.FromNativeResource( lightProbeVolume.m_hLightProbeDirectLightIndicesTexture ) : null);
	internal Texture ScalarsTexture => scalarsTexture ?? (lightProbeVolume.IsValid ? Texture.FromNativeResource( lightProbeVolume.m_hLightProbeDirectLightScalarsTexture ) : null);

	/// <summary>The handshake objects the map compiler bound to this volume carry (<c>m_nHandshake</c>).</summary>
	internal int Handshake => made?.Handshake ?? (lightProbeVolume.IsValid ? lightProbeVolume.m_nHandshake : 0);

	/// <summary>The box it lights, in its own space.</summary>
	internal Vector3 BoxMins => made?.Mins ?? (lightProbeVolume.IsValid ? lightProbeVolume.m_vBoxMins : default);
	internal Vector3 BoxMaxs => made?.Maxs ?? (lightProbeVolume.IsValid ? lightProbeVolume.m_vBoxMaxs : default);

	/// <summary>Of two volumes holding an object, the higher priority lights it.</summary>
	internal int RenderPriority => made?.Priority ?? (lightProbeVolume.IsValid ? lightProbeVolume.m_nRenderPriority : 0);

	/// <summary>The light groups it lights, by token: none for one managed code made, native's (Hammer's) otherwise.</summary>
	internal uint[] LightGroups
	{
		get
		{
			if ( made is not null || !lightProbeVolume.IsValid ) return [];
			var groups = new uint[lightProbeVolume.GetLightGroupCount()];
			for ( int i = 0; i < groups.Length; i++ ) groups[i] = lightProbeVolume.GetLightGroup( i );
			return groups;
		}
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
