using Sandbox.Rendering;

namespace Sandbox;

/// <summary>
/// Applies a depth of field effect to the camera
/// </summary>
[Title( "Depth Of Field" )]
[Category( "Post Processing" )]
[Icon( "center_focus_strong" )]
public sealed class DepthOfField : BasePostProcess<DepthOfField>
{
	/// <summary>
	/// Quality scale factors: [Off, Low, Medium, High]
	/// </summary>
	private static readonly float[] StepScales = { 0f, 3f, 2f, 1f };

	// same problem, this isn't granular enough
	[ConVar( "r_dof_quality", ConVarFlags.Saved, Min = 0, Max = 3, Help = "Depth of field quality (0: off, 1: low, 2: med, 3: high)" )]
	internal static int Quality { get; set; } = 3;

	/// <summary>
	/// How blurry to make stuff that isn't in focus.
	/// </summary>
	[Range( 0, 100 )]
	[Property, Group( "Focus" ), Icon( "blur_circular" )]
	public float BlurSize { get; set; } = 30.0f;

	/// <summary>
	/// How far away from the camera to focus in world units.
	/// </summary>
	[Range( 1.0f, 1000 )]
	[Property, Group( "Focus" ), Icon( "horizontal_distribute" )]
	public float FocalDistance { get; set; } = 200.0f;

	/// <summary>
	/// This modulates how far is the blur to the image.
	/// </summary>
	[Property, Range( 0.0f, 1000.0f ), Group( "Focus" ), Icon( "blur_linear" )]
	public float FocusRange { get; set; } = 500f;

	/// <summary>
	/// Should we blur what's ahead the focal point towards us?
	/// </summary>
	[Property, Group( "Properties" ), Icon( "flip_to_back" ), Hide]
	public bool FrontBlur { get; set; } = false;

	/// <summary>
	/// Should we blur what's behind the focal point?
	/// </summary>
	[Property, Group( "Properties" ), Icon( "flip_to_front" ), Hide]
	public bool BackBlur { get; set; } = true;

	CommandList command = new CommandList( "Depth Of Field" );

	private static readonly ComputeShader ShaderCs = new ComputeShader( "postprocess_standard_dof_cs" );

	private static readonly Material Shader = Material.FromShader( "postprocess_standard_dof.shader" );

	public override void Render()
	{
		if ( Quality == 0 || (!BackBlur && !FrontBlur) )
			return;

		float blurSize = GetWeighted( x => x.BlurSize, 0.0f );
		if ( blurSize < 0.5f ) return;

		float focalDistance = GetWeighted( x => x.FocalDistance, 200.0f );
		float focalLength = GetWeighted( x => x.FocusRange, 500.0f );

		float stepScale = StepScales[Quality.Clamp( 0, 3 )];

		command.Reset();

		var downsample = 2;

		command.Attributes.SetValue( "Color", RenderValue.ColorTarget );
		command.Attributes.SetValue( "Depth", RenderValue.DepthTarget );
		command.Attributes.SetValue( "D_MSAA", RenderValue.MsaaCombo );

		var Vertical = command.GetRenderTarget( "Vertical", downsample, ImageFormat.RGBA16161616F, ImageFormat.None );
		var Diagonal = command.GetRenderTarget( "Diagonal", downsample, ImageFormat.RGBA16161616F, ImageFormat.None );
		var Final = command.GetRenderTarget( "Final", downsample, ImageFormat.RGBA16161616F, ImageFormat.None );

		command.Attributes.Set( "InvDimensions", Vertical.Size, true );
		command.Attributes.Set( "Radius", (int)(blurSize / stepScale) );

		command.Attributes.Set( "StepScale", stepScale );

		command.Attributes.Set( "VerticalSRV", Vertical.ColorTexture );
		command.Attributes.Set( "DiagonalSRV", Diagonal.ColorTexture );
		command.Attributes.Set( "FinalSRV", Final.ColorTexture );

		command.Attributes.Set( "Vertical", Vertical.ColorTexture );
		command.Attributes.Set( "Diagonal", Diagonal.ColorTexture );
		command.Attributes.Set( "Final", Final.ColorTexture );

		command.Attributes.Set( "FocusGap", 0 );

		foreach ( DoFTypes type in Enum.GetValues( typeof( DoFTypes ) ) )
		{
			if ( !BackBlur && type == DoFTypes.BackBlur )
				continue;

			if ( !FrontBlur && type == DoFTypes.FrontBlur )
				continue;

			command.Attributes.Set( "FocusPlane", focalDistance.Clamp( 0, 5000 ) );
			command.Attributes.Set( "FocalLength", focalLength );
			command.Attributes.SetCombo( "D_DOF_TYPE", type );

			command.Attributes.SetCombo( "D_PASS", BlurPasses.CircleOfConfusion );
			command.DispatchCompute( ShaderCs, Vertical.Size );

			command.Attributes.SetCombo( "D_PASS", BlurPasses.Blur );
			command.DispatchCompute( ShaderCs, Vertical.Size );

			command.Attributes.SetCombo( "D_PASS", BlurPasses.RhomboidBlur );
			command.DispatchCompute( ShaderCs, Vertical.Size );

			command.Attributes.SetCombo( "D_DOF_TYPE", type );
			command.Attributes.SetCombo( "D_PASS", 0 );

			command.Blit( Shader );
		}

		InsertCommandList( command, Stage.AfterTransparent, 100, "Dof" );
	}

	private enum BlurPasses
	{
		CircleOfConfusion,
		Blur,
		RhomboidBlur,
	};

	private enum DoFTypes
	{
		BackBlur,
		FrontBlur,
	};
}
