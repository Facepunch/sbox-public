using Sandbox.Rendering;

/// <summary>
/// Draws the scene in two colors with a screen-aligned ordered dither pattern.
/// </summary>
[PostProcessMenu, Title( "#asset.onebitdither.title" ), Icon( "contrast" ), ClassName( "onebitdither" )]
[Group( "#effects.group.art_styles" )]
public sealed class OneBitDither : BasePostProcess
{
	/// <summary>
	/// Width and height of a dither pixel in screen pixels.
	/// </summary>
	[Property, Range( 1, 12 )]
	public int PixelSize { get; set; } = 2;

	/// <summary>
	/// Brightness midpoint. Raise it to give shadows more of the image.
	/// </summary>
	[Property, Range( 0, 1 )]
	public float Threshold { get; set; } = 0.5f;

	/// <summary>
	/// Spread of the ordered thresholds. Zero gives a hard two-color silhouette.
	/// </summary>
	[Property, Range( 0, 1 )]
	public float DitherAmount { get; set; } = 1.0f;

	/// <summary>
	/// Color of shadow pixels.
	/// </summary>
	[Property]
	public Color Ink { get; set; } = Color.Black;

	/// <summary>
	/// Color of highlight pixels.
	/// </summary>
	[Property]
	public Color Paper { get; set; } = Color.White;

	CommandList _pass;

	/// <summary>
	/// Applies 1-Bit Dither to the finished camera image.
	/// </summary>
	public override void Render()
	{
		Attributes.Set( "PixelSize", PixelSize );
		Attributes.Set( "Threshold", Threshold );
		Attributes.Set( "DitherAmount", DitherAmount );
		Attributes.Set( "Ink", new Vector4( Ink ) );
		Attributes.Set( "Paper", new Vector4( Paper ) );

		_pass ??= PostProcessPass.FromShader( "postprocess/onebitdither/onebitdither.shader", Attributes );
		InsertCommandList( _pass, Stage.AfterPostProcess, 220, "1-Bit Dither" );
	}
}
