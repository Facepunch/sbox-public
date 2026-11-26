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
		int renderPriority,
		float atlasZScale = 1.0f,
		float atlasZOffset = 0.0f ) : base()
	{
		Assert.IsValid( sceneWorld );

		// As native's CLightProbeVolumeObjectDesc::Create made it: its flags, and no bounds until they're set
		MakeManaged( sceneWorld, null, Rendering.SceneObjectFlags.IsLightVolume | Rendering.SceneObjectFlags.IsLight | Rendering.SceneObjectFlags.IsIndirectLight );
		KeepFixedBounds( false );

		probeTexture = texture;
		this.indicesTexture = indicesTexture;
		this.scalarsTexture = scalarsTexture;
		made = (bounds.Mins, bounds.Maxs, handshake, renderPriority, Math.Max( atlasZScale, float.Epsilon ), atlasZOffset);

		LocalBounds = bounds;
		Transform = transform;

		RenderingEnabled = true;

		// Its box where it's placed (later moves leave them, as native's PlaceInWorld bounds Hammer's)
		KeepBounds( TransformBounds( Transform, bounds ) );
	}

	readonly Texture probeTexture, indicesTexture, scalarsTexture;

	// What managed code made it with; null for a volume native made (Hammer's), whose settings native keeps
	readonly (Vector3 Mins, Vector3 Maxs, int Handshake, int Priority, float AtlasZScale, float AtlasZOffset)? made;

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

	/// <summary>The fraction of its textures' depth that is this volume's, when volumes share an atlas (1: all of it).</summary>
	internal float AtlasZScale => made?.AtlasZScale ?? (lightProbeVolume.IsValid ? lightProbeVolume.m_flAtlasZScale : 1.0f);

	/// <summary>Where this volume's part of its textures' depth starts, when volumes share an atlas (0: at the start).</summary>
	internal float AtlasZOffset => made?.AtlasZOffset ?? (lightProbeVolume.IsValid ? lightProbeVolume.m_flAtlasZOffset : 0.0f);

	/// <summary>Of two volumes holding an object, the higher priority lights it.</summary>
	internal int RenderPriority => made?.Priority ?? (lightProbeVolume.IsValid ? lightProbeVolume.m_nRenderPriority : 0);

	/// <summary>
	/// The light groups it lights, by token: the default group ("", token 0) for one managed code made, as native's volume
	/// object always has, so objects in no group of their own are lit by it; native's (Hammer's) otherwise.
	/// </summary>
	internal uint[] LightGroups
	{
		get
		{
			if ( made is not null || !lightProbeVolume.IsValid ) return [0];
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
