using NativeEngine;

namespace Sandbox;

/// <summary>
/// A decal. Use the Component.
/// </summary>
internal sealed class DecalSceneObject : SceneObject
{
	NativeEngine.CDecalSceneObject decalNative;

	internal DecalSceneObject() { }
	internal DecalSceneObject( HandleCreationData _ ) { }

	public Texture ColorTexture
	{
		get => Texture.FromNative( decalNative.m_hColor );
		set => decalNative.m_hColor = value?.native ?? default;
	}

	public Texture NormalTexture
	{
		get => Texture.FromNative( decalNative.m_hNormal );
		set => decalNative.m_hNormal = value?.native ?? default;
	}

	public Texture RMOTexture
	{
		get => Texture.FromNative( decalNative.m_hRMO );
		set => decalNative.m_hRMO = value?.native ?? default;
	}

	public Texture HeightTexture
	{
		get => Texture.FromNative( decalNative.m_hHeight );
		set => decalNative.m_hHeight = value?.native ?? default;
	}

	public Color Color
	{
		get => decalNative.m_vColorTint;
		set => decalNative.m_vColorTint = value;
	}

	public uint SortOrder
	{
		get => decalNative.m_nSortOrder;
		set => decalNative.m_nSortOrder = value;
	}

	public uint ExclusionBitMask
	{
		get => decalNative.m_nExclusionBitMask;
		set => decalNative.m_nExclusionBitMask = value;
	}

	public Texture EmissionTexture
	{
		get => Texture.FromNative( decalNative.m_hEmission );
		set => decalNative.m_hEmission = value?.native ?? default;
	}

	public float AttenuationAngle
	{
		get => decalNative.m_flAttenuationAngle;
		set => decalNative.m_flAttenuationAngle = value;
	}

	public float ColorMix
	{
		get => decalNative.m_flColorMix;
		set => decalNative.m_flColorMix = value;
	}

	public float EmissionEnergy
	{
		get => decalNative.m_flEmissionEnergy;
		set => decalNative.m_flEmissionEnergy = value;
	}

	public uint SequenceIndex
	{
		get => decalNative.m_nSequenceIndex;
		set => decalNative.m_nSequenceIndex = value;
	}

	public float ParallaxStrength
	{
		get => decalNative.m_flParallaxStrength;
		set => decalNative.m_flParallaxStrength = value;
	}

	public int SamplerIndex
	{
		get => decalNative.m_nSamplerIndex;
		set => decalNative.m_nSamplerIndex = value;
	}

	public float CoverageAmount
	{
		get => decalNative.m_flCoverageAmount;
		set => decalNative.m_flCoverageAmount = value;
	}

	public float CoverageRange
	{
		get => decalNative.m_flCoverageRange;
		set => decalNative.m_flCoverageRange = value;
	}

	public DecalSceneObject( SceneWorld world )
	{
		Assert.IsValid( world );

		using ( var h = IHandle.MakeNextHandle( this ) )
		{
			CSceneSystem.CreateDecal( world );
		}
	}

	internal override void OnNativeInit( CSceneObject ptr )
	{
		decalNative = (NativeEngine.CDecalSceneObject)ptr;
		base.OnNativeInit( ptr );
	}

	internal override void OnNativeDestroy()
	{
		decalNative = default;
		base.OnNativeDestroy();
	}
}
