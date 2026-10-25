using Sandbox.Rendering;

namespace Sandbox;

/// <summary>
/// Applies color adjustments to the camera.
/// </summary>
[Title( "Color Adjustments" )]
[Category( "Post Processing" )]
[Icon( "palette" )]
public sealed class ColorAdjustments : BasePostProcess<ColorAdjustments>
{
	[Range( 0, 1 ), Property] public float Blend { get; set; } = 1.0f;
	[Range( 0, 2 ), Property] public float Saturation { get; set; } = 1.0f;
	[Range( 0, 360 ), Property] public float HueRotate { get; set; } = 0.0f;
	[Range( 0, 2 ), Property] public float Brightness { get; set; } = 1.0f;
	[Range( 0, 2 ), Property] public float Contrast { get; set; } = 1.0f;

	private static Material Shader = Material.FromShader( "shaders/postprocess/pp_color.shader" );

	public override void Render()
	{
		var blend = GetWeighted( x => x.Blend, 0 );
		if ( blend == 0.0f ) return;

		var saturation = GetWeighted( x => x.Saturation, 1 );
		var hueRotate = GetWeighted( x => x.HueRotate, 0, true );
		var brightness = GetWeighted( x => x.Brightness, 1 );
		var contrast = GetWeighted( x => x.Contrast, 1 );

		// Neutral adjustments don't need a frame copy or a fullscreen pass.
		if ( saturation == 1.0f && hueRotate == 0.0f && brightness == 1.0f && contrast == 1.0f ) return;

		Attributes.Set( "blend", blend );
		Attributes.Set( "saturate", saturation );
		Attributes.Set( "hue_rotate", hueRotate );
		Attributes.Set( "brightness", brightness );
		Attributes.Set( "contrast", contrast );

		var blit = BlitMode.WithBackbuffer( Shader, Stage.AfterPostProcess, 3000, false );
		Blit( blit, "ColorAdjustments" );
	}

}
