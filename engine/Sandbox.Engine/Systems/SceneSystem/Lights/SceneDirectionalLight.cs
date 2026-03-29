using System;
using NativeEngine;

namespace Sandbox;

/// <summary>
/// A directional scene light that is used to mimic sun light in a <see cref="SceneWorld"/>.
/// Direction is controlled by this object's <see cref="Rotation"/>.
/// </summary>
[Expose]
public sealed class SceneDirectionalLight : SceneLight
{
	/// <summary>
	/// Ambient light color outside of all light probes.
	/// </summary>
	[Obsolete( "Use AmbientLight Component or World.AmbientLightColor Instead." )] public Color SkyColor { get; set; }

	internal SceneDirectionalLight( HandleCreationData d ) : base( d )
	{
	}

	public SceneDirectionalLight( SceneWorld sceneWorld, Rotation rotation, Color color ) : base()
	{
		Assert.IsValid( sceneWorld );

		using ( var h = IHandle.MakeNextHandle( this ) )
		{
			CSceneSystem.CreateDirectionalLight( sceneWorld, rotation.Backward );
		}

		LightColor = color;
	}

	/// <summary>
	/// Control number of shadow cascades
	/// </summary>
	public int ShadowCascadeCount
	{
		get { return lightNative.GetShadowCascades(); }
		set { lightNative.SetShadowCascades( value ); }
	}

	public float ShadowCascadeSplitRatio
	{
		get { return lightNative.GetShadowCascadeSplitRatio(); }
		set { lightNative.SetShadowCascadeSplitRatio( value ); }
	}

	/// <summary>
	/// Set the max distance of the shadow cascade
	/// </summary>
	public void SetCascadeDistanceScale( float distance )
	{
		lightNative.SetCascadeDistanceScale( distance );
	}
}
