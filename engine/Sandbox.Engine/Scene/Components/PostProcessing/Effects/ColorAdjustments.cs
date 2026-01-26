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

	private static readonly Material Shader = Material.FromShader( "shaders/postprocess/pp_color.shader" );

	public override void Render()
	{
		Attributes.Set( "blend", GetWeighted( x => x.Blend, 0 ) );
		Attributes.Set( "saturate", GetWeighted( x => x.Saturation, 1 ) );
		Attributes.Set( "hue_rotate", GetWeighted( x => x.HueRotate, 0, true ) );
		Attributes.Set( "brightness", GetWeighted( x => x.Brightness, 1 ) );
		Attributes.Set( "contrast", GetWeighted( x => x.Contrast, 1 ) );

		var blit = BlitMode.WithBackbuffer( Shader, Stage.AfterPostProcess, 3000, false );
		Blit( blit, "ColorAdjustments" );
	}

}
