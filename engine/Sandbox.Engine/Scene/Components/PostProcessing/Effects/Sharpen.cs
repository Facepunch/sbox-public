using Sandbox.Rendering;

namespace Sandbox;

/// <summary>
/// Applies a sharpen effect to the camera
/// </summary>
[Title( "Sharpen" )]
[Category( "Post Processing" )]
[Icon( "deblur" )]
public sealed class Sharpen : BasePostProcess<Sharpen>
{
	[Range( 0, 5 )]
	[Property] public float Scale { get; set; } = 2;

	[Range( 0, 5 )]
	[Property] public float TexelSize { get; set; } = 1;

	public override void Render()
	{
		float scale = GetWeighted( x => x.Scale );

		if ( scale <= 0f )
			return;

		Attributes.Set( "SharpenStrength", scale );
		Attributes.Set( "SharpenSize", GetWeighted( x => x.TexelSize ) );

		AddUberEffect( UberPostEffect.Sharpen, Stage.AfterPostProcess, 1, "Sharpen" );
	}
}
