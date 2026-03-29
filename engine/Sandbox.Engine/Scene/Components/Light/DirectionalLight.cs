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

	public class CascadeVisualizer
	{
		public Action Update;
	}

	/// <summary>
	/// Number of cascades to split the view frustum into for the whole scene dynamic shadow.  
	/// More cascades result in better shadow resolution, but adds significant rendering cost.
	/// 
	/// User settings will set a maximum.
	/// </summary>
	[Property, Group( "Shadows" ), Title( "Cascade Count" ), Range( 1, 4 ), MakeDirty]
	[InfoBox( "More cascades gives better detail at the cost of performance. User quality settings override this." )]
	public int ShadowCascadeCount { get; set; } = 4;

	/// <summary>
	/// Controls how cascades 2+ are distributed between the first cascade boundary and the far clip.
	/// 0 is uniform, 1 is fully logarithmic.
	/// </summary>
	[Property, Group( "Shadows" ), Title( "Split ratio" ), Range( 0, 1 ), MakeDirty, HideIf( nameof( ShadowCascadeCount ), 1 )]
	public float ShadowCascadeSplitRatio { get; set; } = 0.91f;

	[Property, Group( "Shadows" ), HideIf( nameof( ShadowCascadeCount ), 1 )]
	public CascadeVisualizer Visualizer { get; set; } = new();

	protected override SceneLight CreateSceneObject()
	{
		var o = new SceneDirectionalLight( Scene.SceneWorld, WorldRotation, LightColor );
		return o;
	}

	protected override void OnDirty()
	{
		base.OnDirty();
		Visualizer.Update?.Invoke();
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
			o.ShadowCascadeCount = ShadowCascadeCount;
			o.ShadowCascadeSplitRatio = ShadowCascadeSplitRatio;
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
