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
	[Property] public Color Tint { get; set; } = Color.White;

	/// <summary>
	/// No longer used, bloom filtering is fixed quality now.
	/// </summary>
	[Hide, Obsolete( "Bloom gamma is no longer used, this property has no effect." )]
	public float Gamma { get; set; } = 2.2f;

	public enum FilterMode
	{
		Bilinear = 0,
		Biquadratic = 1
	}

	/// <summary>
	/// No longer used, bloom filtering is fixed quality now.
	/// </summary>
	[Hide, Obsolete( "Bloom filtering is fixed quality now, this property has no effect." )]
	public FilterMode Filter { get; set; } = FilterMode.Bilinear;

	CommandList command = new CommandList( "Bloom" );

	private static Material Shader = Material.FromShader( "shaders/postprocess/postprocess_bloom.shader" );

	private static ComputeShader ShaderCs = new ComputeShader( "postprocess/postprocess_bloom_cs" );

	// Half res down to 1/64th res
	const int MipCount = 6;
	static readonly string[] MipNames = ["BloomMip0", "BloomMip1", "BloomMip2", "BloomMip3", "BloomMip4", "BloomMip5"];

	public override void Render()
	{
		if ( Strength == 0.0f || !UserEnabled )
			return;

		command.Reset();

		// Grab the current frame color once and reuse for compute + composite, no mips needed
		var colorHandle = command.Attributes.GrabFrameTexture( "ColorBuffer" );

		// Progressive downsample chain, half res down to 1/64th res
		for ( int i = 0; i < MipCount; i++ )
			command.GetRenderTarget( MipNames[i], 2 << i, ImageFormat.RGBA16161616F, ImageFormat.None );

		static RenderTargetHandle Mip( int i ) => new() { Name = MipNames[i] };

		command.Attributes.Set( "Threshold", GetWeighted( x => x.Threshold, 0 ) );

		// Prefilter: threshold + firefly suppression, full res -> half res
		command.Attributes.SetCombo( "D_STAGE", 0 );
		command.Attributes.Set( "InputTexture", colorHandle.ColorTexture );
		command.Attributes.Set( "BloomOut", Mip( 0 ).ColorTexture );
		command.DispatchCompute( ShaderCs, Mip( 0 ).Size );
		command.ResourceBarrierTransition( Mip( 0 ), ResourceState.NonPixelShaderResource );

		// Downsample chain
		command.Attributes.SetCombo( "D_STAGE", 1 );
		for ( int i = 1; i < MipCount; i++ )
		{
			command.Attributes.Set( "InputTexture", Mip( i - 1 ).ColorTexture );
			command.Attributes.Set( "BloomOut", Mip( i ).ColorTexture );
			command.DispatchCompute( ShaderCs, Mip( i ).Size );
			command.ResourceBarrierTransition( Mip( i ), ResourceState.NonPixelShaderResource );
		}

		// Upsample chain, tent filter accumulating back into each level
		command.Attributes.SetCombo( "D_STAGE", 2 );
		for ( int i = MipCount - 2; i >= 0; i-- )
		{
			command.Attributes.Set( "InputTexture", Mip( i + 1 ).ColorTexture );
			command.Attributes.Set( "BloomOut", Mip( i ).ColorTexture );
			command.DispatchCompute( ShaderCs, Mip( i ).Size );
			command.ResourceBarrierTransition( Mip( i ), i == 0 ? ResourceState.PixelShaderResource : ResourceState.NonPixelShaderResource );
		}

		// Composite: sample the bloom texture in PS and apply selected mode
		command.Attributes.Set( "BloomTexture", Mip( 0 ).ColorTexture );
		command.Attributes.Set( "CompositeMode", (int)Mode );
		command.Attributes.Set( "BloomScale", GetWeighted( x => x.Strength, 0 ) / (2.0f * MipCount) );
		command.Attributes.Set( "Tint", GetWeighted( x => x.Tint, Color.White ) );

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
