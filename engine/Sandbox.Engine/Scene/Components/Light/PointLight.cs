using NativeEngine;

namespace Sandbox;

/// <summary>
/// Emits light in all directions from a point in space.
/// </summary>
[Expose]
[Title( "Point Light" )]
[Category( "Light" )]
[Icon( "light_mode" )]
[EditorHandle( "materials/gizmo/pointlight.png" )]
[Alias( "PointLightComponent" )]
public class PointLight : Light
{
	ScenePointLight _so;

	/// <summary>
	/// Constant term in the light's distance attenuation denominator.
	/// </summary>
	[Property, Group( "Influence", Order = 0 ), Order( 3 ), Advanced]
	public float ConstantAttenuation
	{
		get;
		set
		{
			field = value;
			if ( _so.IsValid() ) _so.ConstantAttenuation = value;
		}
	}

	/// <summary>
	/// Linear term in the light's distance attenuation denominator.
	/// </summary>
	[Property, Group( "Influence", Order = 0 ), Order( 4 ), Advanced]
	public float LinearAttenuation
	{
		get;
		set
		{
			field = value;
			if ( _so.IsValid() ) _so.LinearAttenuation = value;
		}
	}

	/// <summary>
	/// Maximum distance reached by this light.
	/// </summary>
	[Property, Group( "Influence", Order = 0 ), Order( 0 )]
	public float Radius
	{
		get;
		set
		{
			if ( field == value ) return;
			field = value;

			if ( _so.IsValid() )
				_so.Radius = value;
		}
	} = 400;

	/// <summary>
	/// Quadratic term controlling how this light fades with distance.
	/// </summary>
	[Property, Range( 0, 10 ), Group( "Influence", Order = 0 ), Order( 1 )]
	public float Attenuation
	{
		get;
		set
		{
			if ( field == value ) return;
			field = value;

			if ( _so.IsValid() )
				_so.QuadraticAttenuation = value;
		}
	} = 1.0f;

	protected override ScenePointLight CreateSceneObject()
	{
		return _so = new ScenePointLight( Scene.SceneWorld, WorldPosition, Radius, LightColor )
		{
			Radius = Radius,
			ConstantAttenuation = ConstantAttenuation,
			LinearAttenuation = LinearAttenuation,
			QuadraticAttenuation = Attenuation
		};
	}

	protected override void OnAwake()
	{
		Tags.Add( "light_point" );

		base.OnAwake();
	}

	protected override void DrawGizmos()
	{
		using var scope = Gizmo.Scope( $"light-{GetHashCode()}" );

		if ( Gizmo.IsSelected )
		{
			Gizmo.Draw.Color = LightColor.WithAlpha( 0.9f );
			Gizmo.Draw.LineSphere( new Sphere( Vector3.Zero, Radius ), 12 );
		}

		if ( Gizmo.IsHovered && Gizmo.Settings.Selection )
		{
			Gizmo.Draw.Color = LightColor.WithAlpha( 0.4f );
			Gizmo.Draw.LineSphere( new Sphere( Vector3.Zero, Radius ), 12 );
		}
	}
}
