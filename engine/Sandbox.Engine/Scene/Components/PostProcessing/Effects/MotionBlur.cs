using Sandbox.Rendering;

namespace Sandbox;

/// <summary>
/// Applies a motion blur effect to the camera
/// </summary>
[Title( "Motion Blur" )]
[Category( "Post Processing" )]
[Icon( "animation" )]
public sealed class MotionBlur : BasePostProcess<MotionBlur>
{
	[ConVar( "r_motionblur_scale", ConVarFlags.Saved, Help = "Enable or disable motion blur effect." )]
	internal static float UserScale { get; set; } = 1.0f;

	[ConVar( "r_motionblur_quality", Min = 0, Max = 3, Help = "0: low, 1: medium, 2: high (defualt), 3: very high" )]
	internal static int Quality { get; set; } = 2;

	[Range( 0, 1 ), Property] public float Scale { get; set; } = 0.05f;

	public override void Render()
	{
		var scale = GetWeighted( x => x.Scale ) * UserScale;

		if ( scale <= 0.0f ) return;

		ReadOnlySpan<int> samples = [4, 8, 12, 16];

		Attributes.Set( "standard.motionblur.scale", scale );
		Attributes.Set( "standard.motionblur.samples", samples[Quality] );
		Attributes.SetCombo( "D_MOTION_BLUR", true );

		var blit = BlitMode.WithBackbuffer( Material.Load( "materials/postprocess/standard_pass1.vmat" ), Stage.BeforePostProcess, 50, false );
		Blit( blit, "MotionBlur" );

		// It reads each pixel's motion, objects' own included
		Camera.PostProcess.ReadsMotionVectors = true;
	}
}
