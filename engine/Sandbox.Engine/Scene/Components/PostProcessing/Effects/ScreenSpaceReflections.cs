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
	public float RoughnessCutoff => 0.5f;

	readonly bool Denoise = true;

	enum Passes
	{
		ClassifyTiles,
		Intersect,
		DenoiseReproject,
		DenoisePrefilter,
		DenoiseResolveTemporal,
		BilateralUpscale
	}

	enum DispatchArgsEntry
	{
		IntersectAndDenoise = 0,
		BilateralUpscale = 1
	}

	CommandList cmd = new CommandList( "ScreenSpaceReflections" );
	CommandList cmdLastframe = new CommandList( "ScreenSpaceReflections (Last Frame)" );

	private static ComputeShader ShaderCs = new ComputeShader( "screen_space_reflections_cs" );
	private static ComputeShader ClassifyShaderCs = new ComputeShader( "screen_space_reflections_classify_cs" );

	private GpuBuffer<uint> ClassifiedTilesBuffer;
	private GpuBuffer<GpuBuffer.IndirectDispatchArguments> DispatchArgsBuffer;

	private void EnsureClassifiedTileBuffers( Texture referenceTexture )
	{
		var width = Math.Max( 1, referenceTexture.Width );
		var height = Math.Max( 1, referenceTexture.Height );

		var ssrWidth = Math.Max( 1, (int)MathF.Ceiling( width / (float)DownsampleRatio ) );
		var ssrHeight = Math.Max( 1, (int)MathF.Ceiling( height / (float)DownsampleRatio ) );

		var groupsX = (ssrWidth + 7) / 8;
		var groupsY = (ssrHeight + 7) / 8;
		var capacity = Math.Max( 1, groupsX * groupsY );

		if ( ClassifiedTilesBuffer is not null && ClassifiedTilesBuffer.ElementCount >= capacity )
			return;

		ClassifiedTilesBuffer?.Dispose();
		DispatchArgsBuffer?.Dispose();

		ClassifiedTilesBuffer = new GpuBuffer<uint>( capacity, GpuBuffer.UsageFlags.Structured, "SSR_ClassifiedTiles" );
		DispatchArgsBuffer = new GpuBuffer<GpuBuffer.IndirectDispatchArguments>( 2, GpuBuffer.UsageFlags.Structured | GpuBuffer.UsageFlags.IndirectDrawArguments, "SSR_IntersectDispatchArgs" );
	}

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
		var lastFrameRt = cmdLastframe.Attributes.GetRenderTarget( "LastFrameColor" )?.ColorTarget ?? Texture.Transparent;
		EnsureClassifiedTileBuffers( lastFrameRt );

		GpuBuffer.IndirectDispatchArguments[] intersectDispatchArgsUpload = [
			// Entry 0: intersect/denoise (1 group per classified tile)
			new()
			{
				ThreadGroupCountX = 0,
				ThreadGroupCountY = 1,
				ThreadGroupCountZ = 1
			},
			// Entry 1: bilateral upscale (groupsPerTile groups per classified tile)
			new()
			{
				ThreadGroupCountX = 0,
				ThreadGroupCountY = 1,
				ThreadGroupCountZ = 1
			}
		];

		DispatchArgsBuffer.SetData( intersectDispatchArgsUpload );

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

		// Common settings for all passes
		cmd.Attributes.Set( "PreviousFrameColorIndex", lastFrameRt.Index );
		cmd.Attributes.Set( "DepthHistoryIndex", DepthHistory.ColorIndex );
		cmd.Attributes.Set( "GBufferHistoryIndex", GBufferHistory.ColorIndex );

		cmd.Attributes.Set( "Radiance0Index", Radiance0.ColorIndex );
		cmd.Attributes.Set( "Radiance1Index", Radiance1.ColorIndex );
		cmd.Attributes.Set( "Variance0Index", Variance0.ColorIndex );
		cmd.Attributes.Set( "Variance1Index", Variance1.ColorIndex );
		cmd.Attributes.Set( "SampleCount0Index", SampleCount0.ColorIndex );
		cmd.Attributes.Set( "SampleCount1Index", SampleCount1.ColorIndex );
		cmd.Attributes.Set( "AverageRadiance0Index", AverageRadiance0.ColorIndex );
		cmd.Attributes.Set( "AverageRadiance1Index", AverageRadiance1.ColorIndex );
		cmd.Attributes.Set( "ReprojectedRadianceIndex", ReprojectedRadiance.ColorIndex );
		cmd.Attributes.Set( "PingIs0", pingPong );

		cmd.Attributes.Set( "RayLength", RayLength.ColorTexture );
		cmd.Attributes.Set( "RoughnessCutoff", RoughnessCutoff );

		cmd.Attributes.Set( "ClassifiedTiles", ClassifiedTilesBuffer );

		// Downsampled size info
		cmd.Attributes.Set( "Scale", 1.0f / (float)DownsampleRatio );
		cmd.Attributes.Set( "ScaleInv", (float)DownsampleRatio );

		// Clear since we are indirect
		cmd.Clear( radiancePing, Color.Transparent );
		if ( needsUpscale )
			cmd.Clear( FullResRadiance, Color.Transparent );

		foreach ( Passes pass in Enum.GetValues( typeof( Passes ) ) )
		{
			if ( !Denoise && pass > Passes.Intersect )
				break;

			switch ( pass )
			{
				case Passes.ClassifyTiles:
					cmd.ResourceBarrierTransition( ClassifiedTilesBuffer, ResourceState.UnorderedAccess );
					cmd.ResourceBarrierTransition( DispatchArgsBuffer, ResourceState.UnorderedAccess );
					cmd.Attributes.Set( "ClassifiedTilesRW", ClassifiedTilesBuffer );
					cmd.Attributes.Set( "IntersectDispatchArgsRW", DispatchArgsBuffer );
					cmd.DispatchCompute( ClassifyShaderCs, ReprojectedRadiance.Size );
					cmd.ResourceBarrierTransition( ClassifiedTilesBuffer, ResourceState.UnorderedAccess, ResourceState.NonPixelShaderResource );
					cmd.ResourceBarrierTransition( DispatchArgsBuffer, ResourceState.UnorderedAccess, ResourceState.IndirectArgument );
					continue;

				case Passes.Intersect:
					cmd.Attributes.Set( "OutRadiance", radiancePing.ColorTexture );
					break;

				case Passes.DenoiseReproject:
					cmd.Attributes.Set( "OutReprojectedRadiance", ReprojectedRadiance.ColorTexture );
					cmd.Attributes.Set( "OutAverageRadiance", averagePing.ColorTexture );
					cmd.Attributes.Set( "OutVariance", variancePing.ColorTexture );
					cmd.Attributes.Set( "OutSampleCount", samplePing.ColorTexture );
					break;

				case Passes.DenoisePrefilter:
					cmd.Attributes.Set( "OutRadiance", radianceHistory.ColorTexture );
					cmd.Attributes.Set( "OutVariance", varianceHistory.ColorTexture );
					cmd.Attributes.Set( "OutSampleCount", sampleHistory.ColorTexture );
					break;

				case Passes.DenoiseResolveTemporal:
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

					cmd.Attributes.Set( "OutRadiance", FullResRadiance.ColorTexture );
					cmd.Attributes.SetCombo( "D_PASS", (int)Passes.BilateralUpscale );
					// Use bilateral entry which has groupsPerTile groups per classified tile.
					cmd.DispatchComputeIndirect( ShaderCs, DispatchArgsBuffer, (int)DispatchArgsEntry.BilateralUpscale );
					cmd.ResourceBarrierTransition( FullResRadiance, ResourceState.NonPixelShaderResource );
					continue;
			}

			if ( pass == Passes.BilateralUpscale )
				continue;

			cmd.Attributes.SetCombo( "D_PASS", (int)pass );

			cmd.DispatchComputeIndirect( ShaderCs, DispatchArgsBuffer, (int)DispatchArgsEntry.IntersectAndDenoise );

			switch ( pass )
			{
				case Passes.Intersect:
					cmd.ResourceBarrierTransition( radiancePing, ResourceState.NonPixelShaderResource );
					break;

				case Passes.DenoiseReproject:
					cmd.ResourceBarrierTransition( ReprojectedRadiance, ResourceState.NonPixelShaderResource );
					cmd.ResourceBarrierTransition( averagePing, ResourceState.NonPixelShaderResource );
					cmd.ResourceBarrierTransition( variancePing, ResourceState.NonPixelShaderResource );
					cmd.ResourceBarrierTransition( samplePing, ResourceState.NonPixelShaderResource );
					break;

				case Passes.DenoisePrefilter:
					cmd.ResourceBarrierTransition( radianceHistory, ResourceState.NonPixelShaderResource );
					cmd.ResourceBarrierTransition( varianceHistory, ResourceState.NonPixelShaderResource );
					cmd.ResourceBarrierTransition( sampleHistory, ResourceState.NonPixelShaderResource );
					break;

				case Passes.DenoiseResolveTemporal:
					cmd.ResourceBarrierTransition( radiancePing, ResourceState.NonPixelShaderResource );
					cmd.ResourceBarrierTransition( variancePing, ResourceState.NonPixelShaderResource );
					cmd.ResourceBarrierTransition( samplePing, ResourceState.NonPixelShaderResource );
					cmd.ResourceBarrierTransition( GBufferHistory, ResourceState.NonPixelShaderResource );
					cmd.ResourceBarrierTransition( DepthHistory, ResourceState.NonPixelShaderResource );
					break;
			}
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
