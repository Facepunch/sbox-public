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
	[Property]
	public float Radius
	{
		get;
		set
		{
			field = value;

			OnPropertyDirty();
		}
	} = 400;
	[Property, Range( 0, 10 )]
	public float Attenuation
	{
		get;
		set
		{
			field = value;

			OnPropertyDirty();
		}
	} = 1.0f;
	//	[Property, MakeDirty] public Texture Cookie { get; set; }

	protected override SceneLight CreateSceneObject()
	{
		return new SceneLight( Scene.SceneWorld, WorldPosition, Radius, LightColor );
	}

	protected override void OnAwake()
	{
		Tags.Add( "light_point" );

		base.OnAwake();
	}

	protected override void UpdateSceneObject( SceneLight o )
	{
		base.UpdateSceneObject( o );

		o.Radius = Radius;
		o.QuadraticAttenuation = Attenuation;
		//	o.LightCookie = Cookie;
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
