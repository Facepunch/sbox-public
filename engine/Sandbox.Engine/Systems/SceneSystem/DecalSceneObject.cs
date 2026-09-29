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
		set { decalNative.m_hColor = value?.native ?? default; NotifyChanged( Rendering.SceneObjectChange.Settings ); }
	}

	public Texture NormalTexture
	{
		get => Texture.FromNative( decalNative.m_hNormal );
		set { decalNative.m_hNormal = value?.native ?? default; NotifyChanged( Rendering.SceneObjectChange.Settings ); }
	}

	public Texture RMOTexture
	{
		get => Texture.FromNative( decalNative.m_hRMO );
		set { decalNative.m_hRMO = value?.native ?? default; NotifyChanged( Rendering.SceneObjectChange.Settings ); }
	}

	public Texture HeightTexture
	{
		get => Texture.FromNative( decalNative.m_hHeight );
		set { decalNative.m_hHeight = value?.native ?? default; NotifyChanged( Rendering.SceneObjectChange.Settings ); }
	}

	public Color Color
	{
		get => decalNative.m_vColorTint;
		set { decalNative.m_vColorTint = value; NotifyChanged( Rendering.SceneObjectChange.Settings ); }
	}

	public uint SortOrder
	{
		get => decalNative.m_nSortOrder;
		set { decalNative.m_nSortOrder = value; NotifyChanged( Rendering.SceneObjectChange.Settings ); }
	}

	public uint ExclusionBitMask
	{
		get => decalNative.m_nExclusionBitMask;
		set { decalNative.m_nExclusionBitMask = value; NotifyChanged( Rendering.SceneObjectChange.Settings ); }
	}

	public Texture EmissionTexture
	{
		get => Texture.FromNative( decalNative.m_hEmission );
		set { decalNative.m_hEmission = value?.native ?? default; NotifyChanged( Rendering.SceneObjectChange.Settings ); }
	}

	public float AttenuationAngle
	{
		get => decalNative.m_flAttenuationAngle;
		set { decalNative.m_flAttenuationAngle = value; NotifyChanged( Rendering.SceneObjectChange.Settings ); }
	}

	public float ColorMix
	{
		get => decalNative.m_flColorMix;
		set { decalNative.m_flColorMix = value; NotifyChanged( Rendering.SceneObjectChange.Settings ); }
	}

	public float EmissionEnergy
	{
		get => decalNative.m_flEmissionEnergy;
		set { decalNative.m_flEmissionEnergy = value; NotifyChanged( Rendering.SceneObjectChange.Settings ); }
	}

	public uint SequenceIndex
	{
		get => decalNative.m_nSequenceIndex;
		set { decalNative.m_nSequenceIndex = value; NotifyChanged( Rendering.SceneObjectChange.Settings ); }
	}

	public float ParallaxStrength
	{
		get => decalNative.m_flParallaxStrength;
		set { decalNative.m_flParallaxStrength = value; NotifyChanged( Rendering.SceneObjectChange.Settings ); }
	}

	public int SamplerIndex
	{
		get => decalNative.m_nSamplerIndex;
		set { decalNative.m_nSamplerIndex = value; NotifyChanged( Rendering.SceneObjectChange.Settings ); }
	}

	public float CoverageAmount
	{
		get => decalNative.m_flCoverageAmount;
		set { decalNative.m_flCoverageAmount = value; NotifyChanged( Rendering.SceneObjectChange.Settings ); }
	}

	public float CoverageRange
	{
		get => decalNative.m_flCoverageRange;
		set { decalNative.m_flCoverageRange = value; NotifyChanged( Rendering.SceneObjectChange.Settings ); }
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
