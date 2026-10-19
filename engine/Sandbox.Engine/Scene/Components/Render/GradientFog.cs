namespace Sandbox;

/// <summary>
/// Legacy gradient fog retained for existing scenes. Use <see cref="Fog"/> for new fog effects.
/// </summary>
[Obsolete( "Use Fog instead." ), Hide]
[Title( "Gradient Fog" )]
[Category( "Rendering" )]
[Icon( "foggy" )]
public class GradientFog : Component, Component.ExecuteInEditor
{

	[Group( "Vertical Fog" )]
	[Property] public Color Color { get; set; } = Color.White;

	[Group( "Vertical Fog" )]
	[Property] public float Height { get; set; } = 100.0f;

	[Group( "Vertical Fog" )]
	[Property] public float VerticalFalloffExponent { get; set; } = 1.0f;

	[Group( "Camera Distance Fade" )]
	[Property] public float StartDistance { get; set; } = 0.0f;

	[Group( "Camera Distance Fade" )]
	[Property] public float EndDistance { get; set; } = 1024.0f;

	[Group( "Camera Distance Fade" )]
	[Property] public float FalloffExponent { get; set; } = 1.0f;

	protected override void OnPreRender()
	{
		var world = Scene?.SceneWorld;
		if ( !world.IsValid() ) return;

		world.GradientFog.Enabled = true;
		world.GradientFog.StartDistance = StartDistance;
		world.GradientFog.EndDistance = EndDistance;
		world.GradientFog.Color = Color.WithAlpha( 1 );
		world.GradientFog.DistanceFalloffExponent = FalloffExponent;
		world.GradientFog.MaximumOpacity = Color.a;
		world.GradientFog.VerticalFalloffExponent = VerticalFalloffExponent;
		world.GradientFog.StartHeight = WorldPosition.z;
		world.GradientFog.EndHeight = WorldPosition.z + MathF.Max( 1, Height );
	}
}
