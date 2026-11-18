using NativeEngine;
using Sandbox.Rendering;

namespace Sandbox;

/// <summary>
/// A decal. Use the Component.
/// </summary>
/// <remarks>
/// Its settings live here; the native twin gets them only while something reads it (<see cref="SceneObject.WritesNative"/>).
/// </remarks>
internal sealed class DecalSceneObject : SceneObject
{
	NativeEngine.CDecalSceneObject decalNative;

	Texture colorTexture, normalTexture, rmoTexture, heightTexture, emissionTexture;
	Vector4 color;
	uint sortOrder, exclusionBitMask, sequenceIndex;
	float attenuationAngle, colorMix, emissionEnergy, parallaxStrength, coverageAmount, coverageRange;
	int samplerIndex;

	public DecalBlendMode BlendMode
	{
		get;
		set
		{
			if ( field == value ) return;
			field = value;
			Attributes.Set( "DecalBlendMode", (int)value );
			NotifyChanged( Rendering.SceneObjectChange.Settings );
		}
	}

	internal DecalSceneObject() { }
	internal DecalSceneObject( HandleCreationData _ ) { }

	public Texture ColorTexture
	{
		get => colorTexture;
		set { colorTexture = value; SettingsChanged(); }
	}

	public Texture NormalTexture
	{
		get => normalTexture;
		set { normalTexture = value; SettingsChanged(); }
	}

	public Texture RMOTexture
	{
		get => rmoTexture;
		set { rmoTexture = value; SettingsChanged(); }
	}

	public Texture HeightTexture
	{
		get => heightTexture;
		set { heightTexture = value; SettingsChanged(); }
	}

	public Color Color
	{
		get => color;
		set { color = value; SettingsChanged(); }
	}

	public uint SortOrder
	{
		get => sortOrder;
		set { sortOrder = value; SettingsChanged(); }
	}

	public uint ExclusionBitMask
	{
		get => exclusionBitMask;
		set { exclusionBitMask = value; SettingsChanged(); }
	}

	public Texture EmissionTexture
	{
		get => emissionTexture;
		set { emissionTexture = value; SettingsChanged(); }
	}

	public float AttenuationAngle
	{
		get => attenuationAngle;
		set { attenuationAngle = value; SettingsChanged(); }
	}

	public float ColorMix
	{
		get => colorMix;
		set { colorMix = value; SettingsChanged(); }
	}

	public float EmissionEnergy
	{
		get => emissionEnergy;
		set { emissionEnergy = value; SettingsChanged(); }
	}

	public uint SequenceIndex
	{
		get => sequenceIndex;
		set { sequenceIndex = value; SettingsChanged(); }
	}

	public float ParallaxStrength
	{
		get => parallaxStrength;
		set { parallaxStrength = value; SettingsChanged(); }
	}

	public int SamplerIndex
	{
		get => samplerIndex;
		set { samplerIndex = value; SettingsChanged(); }
	}

	public float CoverageAmount
	{
		get => coverageAmount;
		set { coverageAmount = value; SettingsChanged(); }
	}

	public float CoverageRange
	{
		get => coverageRange;
		set { coverageRange = value; SettingsChanged(); }
	}

	public DecalSceneObject( SceneWorld world )
	{
		Assert.IsValid( world );

		using ( var h = IHandle.MakeNextHandle( this ) )
		{
			CSceneSystem.CreateDecal( world );
		}
	}

	void SettingsChanged()
	{
		if ( WritesNative && !decalNative.IsNull ) WriteNative();
		NotifyChanged( Rendering.SceneObjectChange.Settings );
	}

	/// <summary>The settings, written to the native twin.</summary>
	void WriteNative()
	{
		decalNative.m_hColor = colorTexture?.native ?? default;
		decalNative.m_hNormal = normalTexture?.native ?? default;
		decalNative.m_hRMO = rmoTexture?.native ?? default;
		decalNative.m_hHeight = heightTexture?.native ?? default;
		decalNative.m_hEmission = emissionTexture?.native ?? default;
		decalNative.m_vColorTint = color;
		decalNative.m_nSortOrder = sortOrder;
		decalNative.m_nExclusionBitMask = exclusionBitMask;
		decalNative.m_flAttenuationAngle = attenuationAngle;
		decalNative.m_flColorMix = colorMix;
		decalNative.m_flEmissionEnergy = emissionEnergy;
		decalNative.m_nSequenceIndex = sequenceIndex;
		decalNative.m_flParallaxStrength = parallaxStrength;
		decalNative.m_nSamplerIndex = samplerIndex;
		decalNative.m_flCoverageAmount = coverageAmount;
		decalNative.m_flCoverageRange = coverageRange;
	}

	/// <summary>The settings a new native twin starts with: native's defaults, or what native code made it with.</summary>
	void ReadNative()
	{
		colorTexture = Texture.FromNative( decalNative.m_hColor );
		normalTexture = Texture.FromNative( decalNative.m_hNormal );
		rmoTexture = Texture.FromNative( decalNative.m_hRMO );
		heightTexture = Texture.FromNative( decalNative.m_hHeight );
		emissionTexture = Texture.FromNative( decalNative.m_hEmission );
		color = decalNative.m_vColorTint;
		sortOrder = decalNative.m_nSortOrder;
		exclusionBitMask = decalNative.m_nExclusionBitMask;
		attenuationAngle = decalNative.m_flAttenuationAngle;
		colorMix = decalNative.m_flColorMix;
		emissionEnergy = decalNative.m_flEmissionEnergy;
		sequenceIndex = decalNative.m_nSequenceIndex;
		parallaxStrength = decalNative.m_flParallaxStrength;
		samplerIndex = decalNative.m_nSamplerIndex;
		coverageAmount = decalNative.m_flCoverageAmount;
		coverageRange = decalNative.m_flCoverageRange;
	}

	internal override void OnNativeInit( CSceneObject ptr )
	{
		decalNative = (NativeEngine.CDecalSceneObject)ptr;
		ReadNative();
		base.OnNativeInit( ptr );
	}

	internal override void OnNativeDestroy()
	{
		decalNative = default;
		base.OnNativeDestroy();
	}
}
