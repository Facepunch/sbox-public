using Sandbox.Rendering;

namespace Sandbox;

/// <summary>
/// Applies a blur effect to the camera.
/// </summary>
[Title( "Blur" )]
[Category( "Post Processing" )]
[Icon( "lens_blur" )]
public sealed class Blur : BasePostProcess<Blur>
{
	[Range( 0, 1 ), Property] public float Size { get; set; } = 1.0f;

	private static readonly Material Shader = Material.FromShader( "shaders/postprocess/pp_blur.shader" );

	public override void Render()
	{
		float size = GetWeighted( x => x.Size );
		if ( size <= 0f ) return;

		Attributes.Set( "size", size );

		var blit = BlitMode.WithBackbuffer( Shader, Stage.BeforePostProcess, 4000, true );
		Blit( blit, "Blur" );
	}
}
