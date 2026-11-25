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

	/// <summary>A sun lights its world (as its sun, or an extra one); it isn't drawn.</summary>
	internal override bool LightsWorld => true;

	public SceneDirectionalLight( SceneWorld sceneWorld, Rotation rotation, Color color ) : base()
	{
		Assert.IsValid( sceneWorld );

		MakeManagedLight( sceneWorld, NewDescription( TypeDirectional, rotation.Backward ) );

		LightColor = color;
	}

	/// <summary>
	/// Control number of shadow cascades
	/// </summary>
	public int ShadowCascadeCount
	{
		get { return Description.ShadowCascadeCount; }
		set { SetCascades( value, ShadowCascadeSplitRatio, Description.CascadeDistanceScale ); NotifyChanged( Rendering.SceneObjectChange.Settings ); }
	}

	public float ShadowCascadeSplitRatio
	{
		get { return Description.ShadowCascadeSplitRatio; }
		set { SetCascades( ShadowCascadeCount, value, Description.CascadeDistanceScale ); NotifyChanged( Rendering.SceneObjectChange.Settings ); }
	}

	/// <summary>
	/// Include static shadow casters independently of the light's baked-light index.
	/// </summary>
	public bool IncludeStaticShadowCasters
	{
		get;
		set
		{
			if ( field == value ) return;
			field = value;
			NotifyChanged( Rendering.SceneObjectChange.Settings );
		}
	}

	/// <summary>
	/// Set the max distance of the shadow cascade
	/// </summary>
	public void SetCascadeDistanceScale( float distance )
	{
		SetCascades( ShadowCascadeCount, ShadowCascadeSplitRatio, distance ); NotifyChanged( Rendering.SceneObjectChange.Settings );
	}
}
