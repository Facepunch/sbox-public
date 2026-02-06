using Sandbox.Rendering;

namespace Sandbox;

/// <summary>
/// Applies a film grain effect to the camera
/// </summary>
[Title( "FilmGrain" )]
[Category( "Post Processing" )]
[Icon( "grain" )]
public sealed class FilmGrain : BasePostProcess<FilmGrain>
{
	[Range( 0, 1 )]
	[Property] public float Intensity { get; set; } = 0.1f;

	[Range( 0, 1 )]
	[Property] public float Response { get; set; } = 0.5f;

	private static readonly Material Shader = Material.FromShader( "shaders/postprocess/pp_filmgrain.shader" );

	public override void Render()
	{
		float intensity = GetWeighted( x => x.Intensity );
		if ( intensity.AlmostEqual( 0.0f ) ) return;

		float response = GetWeighted( x => x.Response, 1 );

		Attributes.Set( "intensity", intensity );
		Attributes.Set( "response", GetWeighted( x => x.Response, 1 ) );

		var blit = BlitMode.WithBackbuffer( Shader, Stage.AfterPostProcess, 200, false );
		Blit( blit, "FilmGrain" );
	}

}
