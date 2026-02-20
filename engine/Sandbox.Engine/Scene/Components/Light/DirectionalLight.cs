namespace Sandbox;

/// <summary>
/// A directional light that casts shadows, like the sun.
/// </summary>
[Expose]
[Title( "Directional Light" )]
[Category( "Light" )]
[Icon( "light_mode" )]
[EditorHandle( "materials/gizmo/directionallight.png" )]
[Alias( "DirectionalLightComponent" )]
public class DirectionalLight : Light
{
	/// <summary>
	/// Color of the ambient sky color
	/// This is kept for long term support, the recommended way to do this is with an Ambient Light component.
	/// </summary>
	[Property]
	public Color SkyColor { get; set; }

	public DirectionalLight()
	{
		LightColor = "#E9FAFF";
	}

	protected override SceneLight CreateSceneObject()
	{
		var o = new SceneDirectionalLight( Scene.SceneWorld, WorldRotation, LightColor );
		return o;
	}

	protected override void OnAwake()
	{
		Tags.Add( "light_directional" );

		base.OnAwake();
	}

	protected override void UpdateSceneObject( SceneLight l )
	{
		base.UpdateSceneObject( l );

		if ( l is SceneDirectionalLight o )
		{
			o.ShadowCascadeCount = 3;
		}
	}
	protected override void DrawGizmos()
	{
		using var scope = Gizmo.Scope( $"light-{GetHashCode()}" );
		Gizmo.Draw.Color = LightColor;

		var segments = 12;
		for ( var i = 0; i < segments; i++ )
		{
			var angle = MathF.PI * 2 * i / segments;
			var off = (MathF.Sin( angle ) * Vector3.Left + MathF.Cos( angle ) * Vector3.Up) * 5.0f;
			Gizmo.Draw.Line( off, off + Vector3.Forward * 30 );
		}
	}
}
