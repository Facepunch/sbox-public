namespace Sandbox;

/// <summary>
/// Emits light in a specific direction in a cone shape.
/// </summary>
[Expose]
[Title( "Spot Light" )]
[Category( "Light" )]
[Icon( "light_mode" )]
[EditorHandle( "materials/gizmo/spotlight.png" )]
[Alias( "SpotLightComponent" )]
public class SpotLight : Light
{
	[Property, MakeDirty] public float Radius { get; set; } = 500;

	[Range( 0, 90 )]
	[Property, MakeDirty] public float ConeOuter { get; set; } = 45;

	[Range( 0, 90 )]
	[Property, MakeDirty] public float ConeInner { get; set; } = 15;

	[Property, MakeDirty, Range( 0, 10 )] public float Attenuation { get; set; } = 1.0f;
	[Property, MakeDirty] public Texture Cookie { get; set; }

	public SpotLight()
	{
		LightColor = "#E9FAFF";
	}

	protected override SceneLight CreateSceneObject()
	{
		return new SceneSpotLight( Scene.SceneWorld, WorldPosition, LightColor );
	}

	protected override void OnAwake()
	{
		Tags.Add( "light_spot" );

		base.OnAwake();
	}

	protected override void UpdateSceneObject( SceneLight o )
	{
		base.UpdateSceneObject( o );

		o.Radius = Radius;
		o.QuadraticAttenuation = Attenuation;
		o.LightCookie = Cookie;
		//o.ShadowTextureResolution = 4096;

		if ( o is SceneSpotLight spot )
		{
			spot.FallOff = 1;
			spot.ConeInner = ConeInner;
			spot.ConeOuter = ConeOuter;
		}
	}

	protected override void DrawGizmos()
	{
		using var scope = Gizmo.Scope( $"light-{GetHashCode()}" );

		if ( !Gizmo.IsSelected && !Gizmo.IsHovered )
			return;

		Gizmo.Draw.Color = LightColor.WithAlpha( Gizmo.IsSelected ? 0.9f : 0.4f );

		var coneAngle = MathX.DegreeToRadian( ConeOuter );
		var radius = Radius * MathF.Sin( coneAngle );
		var center = Vector3.Forward * Radius * MathF.Cos( coneAngle );
		var startPoint = Vector3.Zero;
		var lastPoint = Vector3.Zero;

		const int segments = 16;

		for ( var i = 0; i < segments; i++ )
		{
			var angle = MathF.PI * 2 * i / segments;
			var currentPoint = center + new Vector3( 0,
				MathF.Cos( angle ) * radius,
				MathF.Sin( angle ) * radius );

			Gizmo.Draw.Line( 0, currentPoint );

			if ( i > 0 )
			{
				Gizmo.Draw.Line( lastPoint, currentPoint );
			}
			else
			{
				startPoint = currentPoint;
			}

			lastPoint = currentPoint;
		}

		Gizmo.Draw.Line( lastPoint, startPoint );
	}
}
