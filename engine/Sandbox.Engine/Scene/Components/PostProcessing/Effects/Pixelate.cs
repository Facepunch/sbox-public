using Sandbox.Rendering;

namespace Sandbox;

/// <summary>
/// Applies a pixelate effect to the camera
/// </summary>
[Title( "Pixelate" )]
[Category( "Post Processing" )]
[Icon( "apps" )]
public sealed class Pixelate : BasePostProcess<Pixelate>
{
	[Range( 0, 1 )]
	[Property] public float Scale { get; set; } = 0.25f;

	private static readonly Material Shader = Material.FromShader( "shaders/postprocess/pp_pixelate.shader" );

	public override void Render()
	{
		float scale = GetWeighted( x => x.Scale );
		if ( scale.AlmostEqual( 0.0f ) ) return;

		Attributes.Set( "scale", scale );

		var blit = BlitMode.WithBackbuffer( Shader, Stage.AfterPostProcess, 10000, true );
		Blit( blit, "Pixelate" );
	}
}
