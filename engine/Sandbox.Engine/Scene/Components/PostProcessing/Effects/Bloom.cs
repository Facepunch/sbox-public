using Sandbox.Rendering;
namespace Sandbox;

using System.Text.Json.Nodes;

/// <summary>
/// Applies a bloom effect to the camera
/// </summary>
[Title( "Bloom" )]
[Category( "Post Processing" )]
[Icon( "exposure" )]
public class Bloom : BasePostProcess<Bloom>
{
	[ConVar( "r_bloom", ConVarFlags.Saved, Help = "Enable or disable bloom effect." )]
	internal static bool UserEnabled { get; set; } = true;

	[Property] public SceneCamera.BloomAccessor.BloomMode Mode { get; set; }

	[Range( 0, 10 )]
	[Property] public float Strength { get; set; } = 1.0f;

	[Range( 0, 2 )]
	[Property] public float Threshold { get; set; } = 1.0f;
	[Property, Range( 1.0f, 2.2f )] public float Gamma { get; set; } = 2.2f;
	[Property] public Color Tint { get; set; } = Color.White;

	public enum FilterMode
	{
		Bilinear = 0,
		Biquadratic = 1
	}

	[Property] public FilterMode Filter { get; set; } = FilterMode.Bilinear;

	CommandList command = new CommandList();

	private static readonly Material Shader = Material.FromShader( "postprocess_bloom.shader" );

	private static readonly ComputeShader ShaderCs = new ComputeShader( "postprocess_bloom_cs" );

	public override void Render()
	{
		if ( Strength == 0.0f || !UserEnabled )
			return;

		command.Reset();

		// Grab the current frame color once and reuse for compute + composite
		var colorHandle = command.Attributes.GrabFrameTexture( "ColorBuffer", true );

		// Half-resolution target for bloom accumulation
		var bloomRt = command.GetRenderTarget( "BloomTexture", 2, ImageFormat.RGBA16161616F, ImageFormat.None ); // Maybe 1010102F?

		// Bind inputs for compute
		command.Attributes.Set( "Color", colorHandle.ColorTexture );

		// Parameters
		command.Attributes.Set( "Strength", GetWeighted( x => x.Strength, 0 ) );
		command.Attributes.Set( "Threshold", GetWeighted( x => x.Threshold, 0 ) );
		command.Attributes.Set( "Gamma", GetWeighted( x => x.Gamma, 0 ) );
		command.Attributes.Set( "Tint", GetWeighted( x => x.Tint, Color.White ) );
		command.Attributes.Set( "InvDimensions", new Vector2( 2.0f / Screen.Width, 2.0f / Screen.Height ) );
		command.Attributes.SetCombo( "D_FILTER", Filter );

		// Output target for compute
		command.Attributes.Set( "BloomOut", bloomRt.ColorTexture );

		// Dispatch compute at bloom RT size
		command.DispatchCompute( ShaderCs, bloomRt.Size );

		// Composite: sample the bloom texture in PS and apply selected mode
		command.Attributes.Set( "BloomTexture", bloomRt.ColorTexture );
		command.Attributes.Set( "CompositeMode", (int)Mode );

		command.Blit( Shader );

		InsertCommandList( command, Stage.BeforePostProcess, 100, "Bloom" );
	}

	public override int ComponentVersion => 1;
	[Expose, JsonUpgrader( typeof( Bloom ), 1 )]
	static void Upgrader_v1( JsonObject obj )
	{
		// Our bloom has a wider range, remap existing ones to an acceptable range
		if ( obj.TryGetPropertyValue( "Threshold", out var frequency ) )
		{
			obj["Threshold"] = ((float)frequency / 2.0f) + 1.0f;
		}
	}
}
