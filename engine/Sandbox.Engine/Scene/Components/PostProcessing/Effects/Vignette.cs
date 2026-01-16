using Sandbox.Rendering;

namespace Sandbox;

/// <summary>
/// Applies a vignette to the camera
/// </summary>
[Title( "Vignette" )]
[Category( "Post Processing" )]
[Icon( "vignette" )]
public sealed class Vignette : BasePostProcess<Vignette>
{
	/// <summary>
	/// The color of the vignette or the "border"
	/// </summary>
	[Property] public Color Color { get; set; } = Color.Black;

	/// <summary>
	/// How strong the vignette is. This is a value between 0 -> 1
	/// </summary>
	[Property, Range( 0, 1 )] public float Intensity { get; set; } = 1.0f;

	/// <summary>
	/// How much fall off or how blurry the vignette is
	/// </summary>
	[Property, Range( 0, 1 )] public float Smoothness { get; set; } = 1.0f;

	/// <summary>
	/// How circular or round the vignette is
	/// </summary>
	[Property, Range( 0, 1 )] public float Roundness { get; set; } = 1.0f;

	/// <summary>
	/// The center of the vignette in relation to UV space. This means
	/// a value of {0.5, 0.5} is the center of the screen
	/// </summary>
	[Property] public Vector2 Center { get; set; } = new Vector2( 0.5f, 0.5f );

	private static readonly Material Shader = Material.FromShader( "shaders/postprocess/pp_vignette.shader" );

	public override void Render()
	{
		float intensity = GetWeighted( x => x.Intensity );
		if ( intensity.AlmostEqual( 0.0f ) ) return;

		var color = GetWeighted( x => x.Color );
		if ( color.a.AlmostEqual( 0.0f ) ) return;

		Attributes.Set( "color", color );
		Attributes.Set( "intensity", intensity );
		Attributes.Set( "smoothness", GetWeighted( x => x.Smoothness, onlyLerpBetweenVolumes: true ) );
		Attributes.Set( "roundness", GetWeighted( x => x.Roundness, onlyLerpBetweenVolumes: true ) );
		Attributes.Set( "center", GetWeighted( x => x.Center, onlyLerpBetweenVolumes: true ) );

		var blit = BlitMode.Simple( Shader, Stage.BeforePostProcess, 5000 );
		Blit( blit, "Vignette" );
	}
}
