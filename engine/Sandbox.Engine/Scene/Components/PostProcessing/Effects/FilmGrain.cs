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

	public override void Render()
	{
		float intensity = GetWeighted( x => x.Intensity );
		if ( intensity.AlmostEqual( 0.0f ) ) return;

		Attributes.Set( "GrainIntensity", intensity );
		Attributes.Set( "GrainResponse", GetWeighted( x => x.Response, 1 ) );

		AddUberEffect( UberPostEffect.FilmGrain, Stage.AfterPostProcess, 200, "FilmGrain" );
	}

}
