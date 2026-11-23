using Sandbox.Rendering;

/// <summary>
/// Prints the scene onto warm paper with four rotated halftone ink screens.
/// </summary>
[PostProcessMenu, Title( "#asset.comicprint.title" ), Icon( "print" ), ClassName( "comicprint" )]
[Group( "#effects.group.art_styles" )]
public sealed class ComicPrint : BasePostProcess
{
	/// <summary>
	/// Amount of the printed image mixed into the scene.
	/// </summary>
	[Property, Range( 0, 1 )]
	public float Strength { get; set; } = 1.0f;

	/// <summary>
	/// Distance in pixels between neighboring ink dots.
	/// </summary>
	[Property, Range( 2, 20 )]
	public float DotSpacing { get; set; } = 5.0f;

	/// <summary>
	/// How far the cyan and magenta plates slip out of alignment, in pixels.
	/// </summary>
	[Property, Range( 0, 6 )]
	public float InkShift { get; set; } = 1.0f;

	CommandList _pass;

	/// <summary>
	/// Draws the printed image over the finished scene.
	/// </summary>
	public override void Render()
	{
		if ( Strength <= 0 ) return;

		Attributes.Set( "Strength", Strength );
		Attributes.Set( "DotSpacing", DotSpacing );
		Attributes.Set( "InkShift", InkShift );

		_pass ??= PostProcessPass.FromShader( "postprocess/comicprint/comicprint.shader", Attributes );
		InsertCommandList( _pass, Stage.AfterPostProcess, 200, "Comic Print" );
	}
}
