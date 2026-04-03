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

	[Property]
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

	[Property, Range( 0, 10 )]
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
