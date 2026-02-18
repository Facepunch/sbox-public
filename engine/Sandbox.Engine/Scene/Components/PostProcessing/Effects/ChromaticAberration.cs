using Sandbox.Rendering;

namespace Sandbox;

/// <summary>
/// Applies a chromatic aberration effect to the camera
/// </summary>
[Title( "Chromatic Aberration" )]
[Category( "Post Processing" )]
[Icon( "zoom_out_map" )]
public sealed class ChromaticAberration : BasePostProcess<ChromaticAberration>
{
	/// <summary>
	/// Strength of the chromatic aberration effect
	/// </summary>
	[Property, Range( 0, 1 )] public float Scale { get; set; } = 0.33f;

	/// <summary>
	/// The pixel offset for each color channel. These values should
	/// be very small as it's in UV space. (0.004 for example)
	/// X = Red
	/// Y = Green
	/// Z = Blue
	/// </summary>
	[Property] public Vector3 Offset { get; set; } = new Vector3( 6f, 2f, 4.0f );

	private static readonly Material Shader = Material.FromShader( "shaders/postprocess/pp_chromaticaberration.shader" );

	public override void Render()
	{
		float scale = GetWeighted( x => x.Scale );
		Vector3 offset = GetWeighted( x => x.Offset );

		if ( scale <= 0f )
			return;

		Attributes.Set( "scale", scale );
		Attributes.Set( "amount", offset / 1000.0f );

		var blit = BlitMode.WithBackbuffer( Shader, Stage.AfterPostProcess, 1000, false );
		Blit( blit, "ChromaticAberration" );
	}
}
