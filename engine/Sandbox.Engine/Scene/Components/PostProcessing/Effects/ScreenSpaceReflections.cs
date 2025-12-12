using Sandbox.Rendering;

namespace Sandbox;

[Title( "Screen-Space Reflections" )]
[Category( "Post Processing" )]
[Icon( "local_mall" )]
public class ScreenSpaceReflections : BasePostProcess<ScreenSpaceReflections>
{
	int Frame;

	Texture BlueNoise { get; set; } = Texture.Load( "textures/dev/blue_noise_256.vtex" );

	[ConVar( "r_ssr_downsample_ratio", Help = "Default SSR resolution scale (0 = Disabled, 1 = Full, 2 = Quarter, 4 = Sixteeneth)." )]
	internal static int DownsampleRatio { get; set; } = 2;

	/// <summary>
	/// Stop tracing rays after this roughness value. 
	/// This is meant to be used to avoid tracing rays for very rough surfaces which are unlikely to have any reflections.
	/// This is a performance optimization.
	/// </summary>
	public float RoughnessCutoff => 0.4f;

	[Property, Hide] public bool Denoise { get; set; } = true;

	enum Passes
	{
		//ClassifyTiles,
		Intersect,
		DenoiseReproject,
		DenoisePrefilter,
		DenoiseResolveTemporal,
		BilateralUpscale
	}

	CommandList cmd = new CommandList( "ScreenSpaceReflections" );
	CommandList cmdLastframe = new CommandList( "ScreenSpaceReflections (Last Frame)" );

	protected override void OnEnabled()
	{
		base.OnEnabled();

		cmdLastframe.Reset();
		cmdLastframe.Attributes.GrabFrameTexture( "LastFrameColor" );
	}

	protected override void OnDisabled()
	{
		base.OnDisabled();
		Frame = 0;
	}

	public override void Render()
	{
		cmd.Reset();

		bool pingPong = (Frame++ % 2) == 0;

		if ( DownsampleRatio < 1 )
			return;

		bool needsUpscale = DownsampleRatio != 1;

		cmd.Attributes.Set( "BlueNoiseIndex", BlueNoise.Index );

		var Radiance0 = cmd.GetRenderTarget( "Radiance0", ImageFormat.RGBA16161616F, sizeFactor: DownsampleRatio );
		var Radiance1 = cmd.GetRenderTarget( "Radiance1", ImageFormat.RGBA16161616F, sizeFactor: DownsampleRatio );

		var Variance0 = cmd.GetRenderTarget( "Variance0", ImageFormat.R16F, sizeFactor: DownsampleRatio );
		var Variance1 = cmd.GetRenderTarget( "Variance1", ImageFormat.R16F, sizeFactor: DownsampleRatio );

		var SampleCount0 = cmd.GetRenderTarget( "Sample Count0", ImageFormat.R16F, sizeFactor: DownsampleRatio );
		var SampleCount1 = cmd.GetRenderTarget( "Sample Count1", ImageFormat.R16F, sizeFactor: DownsampleRatio );

		var AverageRadiance0 = cmd.GetRenderTarget( "Average Radiance0", ImageFormat.RGBA8888, sizeFactor: 8 * DownsampleRatio );
		var AverageRadiance1 = cmd.GetRenderTarget( "Average Radiance1", ImageFormat.RGBA8888, sizeFactor: 8 * DownsampleRatio );

		var ReprojectedRadiance = cmd.GetRenderTarget( "Reprojected Radiance", ImageFormat.RGBA16161616F, sizeFactor: DownsampleRatio );

		var RayLength = cmd.GetRenderTarget( "Ray Length", ImageFormat.R16F, sizeFactor: DownsampleRatio );
		var DepthHistory = cmd.GetRenderTarget( "Previous Depth", ImageFormat.R16F, sizeFactor: DownsampleRatio );
		var GBufferHistory = cmd.GetRenderTarget( "Previous GBuffer", ImageFormat.RGBA16161616F, sizeFactor: DownsampleRatio );
		var FullResRadiance = needsUpscale ? cmd.GetRenderTarget( "Radiance Full", ImageFormat.RGBA16161616F ) : default;

		var radiancePing = pingPong ? Radiance0 : Radiance1;
		var radianceHistory = pingPong ? Radiance1 : Radiance0;

		var variancePing = pingPong ? Variance0 : Variance1;
		var varianceHistory = pingPong ? Variance1 : Variance0;

		var samplePing = pingPong ? SampleCount0 : SampleCount1;
		var sampleHistory = pingPong ? SampleCount1 : SampleCount0;

		var averagePing = pingPong ? AverageRadiance0 : AverageRadiance1;
		var averageHistory = pingPong ? AverageRadiance1 : AverageRadiance0;

		ComputeShader reflectionsCs = new ComputeShader( "screen_space_reflections_cs" );

		var lastFrameRt = cmdLastframe.Attributes.GetRenderTarget( "LastFrameColor" )?.ColorTarget ?? Texture.Transparent;

		// Common settings for all passes
		cmd.Attributes.Set( "GBufferHistory", GBufferHistory.ColorTexture );
		cmd.Attributes.Set( "PreviousFrameColor", lastFrameRt );
		cmd.Attributes.Set( "DepthHistory", DepthHistory.ColorTexture );

		cmd.Attributes.Set( "RayLength", RayLength.ColorTexture );
		cmd.Attributes.Set( "RoughnessCutoff", RoughnessCutoff );

		// Downsampled size info
		cmd.Attributes.Set( "Scale", 1.0f / (float)DownsampleRatio );
		cmd.Attributes.Set( "ScaleInv", (float)DownsampleRatio );

		foreach ( Passes pass in Enum.GetValues( typeof( Passes ) ) )
		{
			if ( !Denoise && pass != Passes.Intersect )
				break;

			switch ( pass )
			{
				case Passes.Intersect:
					cmd.Attributes.Set( "OutRadiance", radiancePing.ColorTexture );
					break;

				case Passes.DenoiseReproject:
					cmd.Attributes.Set( "Radiance", radiancePing.ColorTexture );
					cmd.Attributes.Set( "RadianceHistory", radianceHistory.ColorTexture );
					cmd.Attributes.Set( "AverageRadianceHistory", averageHistory.ColorTexture );
					cmd.Attributes.Set( "VarianceHistory", varianceHistory.ColorTexture );
					cmd.Attributes.Set( "SampleCountHistory", sampleHistory.ColorTexture );

					cmd.Attributes.Set( "OutReprojectedRadiance", ReprojectedRadiance.ColorTexture );
					cmd.Attributes.Set( "OutAverageRadiance", averagePing.ColorTexture );
					cmd.Attributes.Set( "OutVariance", variancePing.ColorTexture );
					cmd.Attributes.Set( "OutSampleCount", samplePing.ColorTexture );
					break;

				case Passes.DenoisePrefilter:
					cmd.Attributes.Set( "Radiance", radiancePing.ColorTexture );
					cmd.Attributes.Set( "RadianceHistory", radianceHistory.ColorTexture );
					cmd.Attributes.Set( "AverageRadiance", averagePing.ColorTexture );
					cmd.Attributes.Set( "Variance", variancePing.ColorTexture );
					cmd.Attributes.Set( "SampleCountHistory", samplePing.ColorTexture );

					cmd.Attributes.Set( "OutRadiance", radianceHistory.ColorTexture );
					cmd.Attributes.Set( "OutVariance", varianceHistory.ColorTexture );
					cmd.Attributes.Set( "OutSampleCount", sampleHistory.ColorTexture );
					break;

				case Passes.DenoiseResolveTemporal:
					cmd.Attributes.Set( "AverageRadiance", averagePing.ColorTexture );
					cmd.Attributes.Set( "Radiance", radianceHistory.ColorTexture );
					cmd.Attributes.Set( "ReprojectedRadiance", ReprojectedRadiance.ColorTexture );
					cmd.Attributes.Set( "Variance", varianceHistory.ColorTexture );
					cmd.Attributes.Set( "SampleCount", sampleHistory.ColorTexture );

					cmd.Attributes.Set( "OutRadiance", radiancePing.ColorTexture );
					cmd.Attributes.Set( "OutVariance", variancePing.ColorTexture );
					cmd.Attributes.Set( "OutSampleCount", samplePing.ColorTexture );

					cmd.Attributes.Set( "GBufferHistoryRW", GBufferHistory.ColorTexture );
					cmd.Attributes.Set( "DepthHistoryRW", DepthHistory.ColorTexture );
					break;

				case Passes.BilateralUpscale:
					if ( !needsUpscale )
					{
						continue;
					}

					cmd.Attributes.Set( "Radiance", radiancePing.ColorTexture );
					cmd.Attributes.Set( "OutRadiance", FullResRadiance.ColorTexture );
					cmd.Attributes.SetCombo( "D_PASS", (int)Passes.BilateralUpscale );
					cmd.DispatchCompute( reflectionsCs, cmd.ViewportSize );
					continue;
			}

			if ( pass == Passes.BilateralUpscale )
				continue;

			cmd.Attributes.SetCombo( "D_PASS", (int)pass );
			cmd.DispatchCompute( reflectionsCs, ReprojectedRadiance.Size );
		}

		var finalReflection = needsUpscale ? FullResRadiance : radiancePing;
		cmd.ResourceBarrierTransition( finalReflection, ResourceState.PixelShaderResource );

		// Final SSR color to be used by shaders
		if ( needsUpscale )
			cmd.GlobalAttributes.Set( "ReflectionColorIndex", FullResRadiance.ColorIndex );
		else
			cmd.GlobalAttributes.Set( "ReflectionColorIndex", radiancePing.ColorIndex );


		InsertCommandList( cmdLastframe, Stage.AfterOpaque, 0, "ScreenSpaceReflections" );
		InsertCommandList( cmd, Stage.AfterDepthPrepass, int.MaxValue, "ScreenSpaceReflections" );
	}

}
