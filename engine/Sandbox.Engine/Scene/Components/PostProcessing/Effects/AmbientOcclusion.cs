using Sandbox.Rendering;

namespace Sandbox;

/// <summary>
/// Adds an approximation of ambient occlusion using Screen Space Ambient Occlusion (SSAO).
/// It darkens areas where ambient light is generally occluded from such as corners, crevices
/// and surfaces that are close to each other.
/// </summary>
[Expose]
[Title( "Ambient Occlusion (SSAO)" )]
[Category( "Post Processing" )]
[Icon( "contrast" )]
public sealed partial class AmbientOcclusion : BasePostProcess<AmbientOcclusion>
{
	public override int ComponentVersion => 1;

	// This is too high level, the convars should be controlling num samples and that shit
	[ConVar( "r_ao_quality", Min = 0, Max = 3, Help = "Ambient occlusion quality (0: off, 1: low, 2: med, 3: high)" )]
	internal static int UserQuality { get; set; } = 3;

	/// <summary>
	/// The intensity of the darkening effect. Has no impact on performance.
	/// </summary>
	[Property, Range( 0, 1 ), Category( "Properties" )]
	public float Intensity { get; set; } = 1.0f;

	/// <summary>
	/// Maximum distance of samples from pixel when determining its occlusion, in world units.
	/// </summary>
	[Property, Range( 1, 512 ), Category( "Properties" )]
	public int Radius { get; set; } = 128;

	/// <summary>
	/// Gently reduce sample impact as it gets out of the effect's radius bounds
	/// </summary>
	[Property, Range( 0.01f, 1.0f ), Category( "Properties" )]
	public float FalloffRange { get; set; } = 1.0f;

	/// <summary>
	/// How we should denoise the effect
	/// </summary>
	public DenoiseModes DenoiseMode { get; set; } = DenoiseModes.Spatial;

	/// <summary>
	/// Run ambient occlusion at a reduced resolution to save GPU time.
	/// The AO texture is sampled with bilinear filtering when applied to the scene.
	/// </summary>
	[ConVar( "r_ao_resolution", Min = 1, Max = 8, Help = "Ambient occlusion resolution scale divisor (1: full, 2: half, 4: quarter, 8: eighth)" )]
	internal static int UserResolution { get; set; } = 2;

	/// <summary>
	/// Slightly reduce impact of samples further back to counter the bias from depth-based (incomplete) input scene geometry data
	/// </summary>
	[Property, Category( "Quality" ), Range( 0.0f, 5.0f )]
	public float ThinCompensation { get; set; } = 5.0f;

	/// <summary>
	/// Blue-noise texture used by GTAO sampling.
	/// </summary>
	Texture BlueNoise { get; set; } = Texture.Load( "textures/dev/blue_noise_256.vtex" );

	int Frame = 0;

	private struct GTAOConstants
	{
		public Vector2Int ViewportSize; // Unused with Command Lists
		public Vector2 ViewportPixelSize;                  // Unused with Command Lists

		public Vector2 DepthUnpackConsts;
		public Vector2 CameraTanHalfFOV;

		public Vector2 NDCToViewMul;
		public Vector2 NDCToViewAdd;

		public Vector2 NDCToViewMul_x_PixelSize;
		public float EffectRadius;                       // world (viewspace) maximum size of the shadow
		public float EffectFalloffRange;

		public float RadiusMultiplier = 1.457f;
		public float TAABlendAmount = 0;
		public float FinalValuePower = 2.2f;             // modifies the final ambient occlusion value using power function - this allows some of the above heuristics to do different things
		public float DenoiseBlurBeta = 1.5f;
		public float SampleDistributionPower = 2.0f;      // small crevices more important than big surfaces
		public float ThinOccluderCompensation = 0.0f;    // the new 'thickness heuristic' approach
		public float DepthMIPSamplingOffset = 3.30f;     // main trade-off between performance (memory bandwidth) and quality (temporal stability is the first affected, thin objects next)
		public int NoiseIndex = 0;            // frameIndex % 64 if using TAA or 0 otherwise
		public GTAOConstants() { }
	};

	enum GTAOPasses
	{
		ViewDepthChain,
		MainPass,
		DenoiseSpatial,
		DenoiseTemporal,
		BilateralUpsample
	}

	//-------------------------------------------------------------------------

	public enum DenoiseModes
	{
		/// <summary>
		/// Applies same-frame multi-pass spatial denoising (dilated edge-aware blur).
		/// This smooths sampling noise without requiring previous frame history.
		/// </summary>
		[Icon( "filter_center_focus" )]
		Spatial,

		/// <summary>
		/// Applies temporal denoising to reduce noise by averaging pixel values over multiple frames.
		/// This method leverages the temporal coherence of consecutive frames to achieve a noise-free result.
		/// </summary>
		[Icon( "auto_awesome_motion" )]
		Temporal
	}

	GTAOConstants GetGTAOConstants()
	{
		var consts = new GTAOConstants();

		// Viewport-dependent values are computed in the shader's GetConstants()
		consts.ViewportSize = Vector2Int.Zero;
		consts.ViewportPixelSize = Vector2.Zero;
		consts.DepthUnpackConsts = Vector2.Zero;
		consts.CameraTanHalfFOV = Vector2.Zero;
		consts.NDCToViewMul = Vector2.Zero;
		consts.NDCToViewAdd = Vector2.Zero;
		consts.NDCToViewMul_x_PixelSize = Vector2.Zero;

		//-------------------------------------------------------------------------
		consts.EffectRadius = GetWeighted( x => x.Radius, 128.0f );
		consts.EffectFalloffRange = GetWeighted( x => x.FalloffRange, 1.0f );
		consts.DenoiseBlurBeta = 1.2f;

		// XeGTAO expects NoiseIndex in [0,63] for the Hilbert R2 sequence
		consts.NoiseIndex = DenoiseMode == DenoiseModes.Temporal ? Frame % 64 : 0;
		consts.ThinOccluderCompensation = ThinCompensation;

		// Map [0,1] intensity to a reasonable power curve.
		// pow(visibility, power) where power=0 gives no darkening, power=4 gives strong AO.
		consts.FinalValuePower = GetWeighted( x => x.Intensity, 1.0f ) * 4.0f;

		// Temporal blend is consistent across quality levels — the quality combo
		// controls slice/step counts in the shader instead.
		if ( UserQuality >= 1 )
			consts.TAABlendAmount = 0.95f;

		return consts;
	}

	CommandList commands = new CommandList( "Ambient Occlusion" );

	private static ComputeShader GtaoCs = new ComputeShader( "gtao_cs" );

	public override void Render()
	{
		if ( UserQuality <= 0 )
			return;

		commands.Reset();

		int scale = UserResolution.Clamp( 1, 8 );

		RenderTargetHandle ViewDepthChainTexture = commands.GetRenderTarget( "ViewDepthChainTexture", ImageFormat.R32F, numMips: 5 );
		RenderTargetHandle WorkingEdgesTexture = commands.GetRenderTarget( "WorkingEdgesTexture", ImageFormat.R16F, sizeFactor: scale );
		RenderTargetHandle WorkingAOTexture = commands.GetRenderTarget( "WorkingAOTexture", ImageFormat.A8, sizeFactor: scale );
		RenderTargetHandle AOTexture0 = commands.GetRenderTarget( "AOTexture0", ImageFormat.A8, sizeFactor: scale );
		RenderTargetHandle AOTexture1 = commands.GetRenderTarget( "AOTexture1", ImageFormat.A8, sizeFactor: scale );

		bool pingPong = (Frame++ % 2) == 0;

		var AOTextureCurrent = pingPong ? AOTexture0 : AOTexture1;
		var AOTexturePrev = pingPong ? AOTexture1 : AOTexture0;

		commands.Attributes.SetData( "GTAOConstants", GetGTAOConstants() );
		commands.Attributes.Set( "ResolutionScale", scale );
		commands.Attributes.Set( "BlueNoiseIndex", BlueNoise?.Index ?? 0 );

		// 
		// Bind textures to the compute shader
		commands.Attributes.Set( "WorkingDepthMIP0", ViewDepthChainTexture.ColorTexture, 0 );
		commands.Attributes.Set( "WorkingDepthMIP1", ViewDepthChainTexture.ColorTexture, 1 );
		commands.Attributes.Set( "WorkingDepthMIP2", ViewDepthChainTexture.ColorTexture, 2 );
		commands.Attributes.Set( "WorkingDepthMIP3", ViewDepthChainTexture.ColorTexture, 3 );
		commands.Attributes.Set( "WorkingDepthMIP4", ViewDepthChainTexture.ColorTexture, 4 );
		commands.Attributes.Set( "WorkingDepth", ViewDepthChainTexture.ColorTexture );
		commands.Attributes.Set( "WorkingAOTerm", WorkingAOTexture.ColorTexture );
		commands.Attributes.Set( "WorkingEdges", WorkingEdgesTexture.ColorTexture );
		commands.Attributes.Set( "FinalAOTerm", AOTextureCurrent.ColorTexture );
		commands.Attributes.Set( "FinalAOTermPrev", AOTexturePrev.ColorTexture );
		commands.Attributes.Set( "SpatialIn", WorkingAOTexture.ColorTexture );
		commands.Attributes.Set( "SpatialOut", AOTextureCurrent.ColorTexture );
		commands.Attributes.Set( "SpatialStep", 1 );

		commands.Attributes.SetCombo( "D_QUALITY", (UserQuality - 1).Clamp( 0, 2 ) );

		// View depth chain — always at full resolution so MIP0 has pixel-exact
		// view-space depth for the bilateral upsampler's edge detection.
		{
			commands.Attributes.Set( "ResolutionScale", 1 );
			commands.Attributes.SetCombo( "D_PASS", GTAOPasses.ViewDepthChain );
			commands.DispatchCompute( GtaoCs, commands.ViewportSizeScaled( 2 ) );
		}

		commands.Attributes.Set( "ResolutionScale", scale );

		commands.ResourceBarrierTransition( ViewDepthChainTexture, ResourceState.NonPixelShaderResource );

		// Main pass
		{
			commands.Attributes.SetCombo( "D_PASS", GTAOPasses.MainPass );
			commands.DispatchCompute( GtaoCs, AOTextureCurrent.Size );
		}

		commands.ResourceBarrierTransition( WorkingAOTexture, ResourceState.NonPixelShaderResource );

		if ( DenoiseMode == DenoiseModes.Temporal )
		{
			commands.ResourceBarrierTransition( WorkingEdgesTexture, ResourceState.NonPixelShaderResource );

			commands.Attributes.SetCombo( "D_PASS", GTAOPasses.DenoiseTemporal );
			commands.DispatchCompute( GtaoCs, AOTextureCurrent.Size );
		}
		else
		{
			// Same-frame multi-pass spatial denoise with dilated steps.
			commands.Attributes.SetCombo( "D_PASS", GTAOPasses.DenoiseSpatial );

			// Pass 1: working AO -> current AO (step 1)
			commands.ResourceBarrierTransition( AOTextureCurrent, ResourceState.UnorderedAccess );
			commands.Attributes.Set( "SpatialIn", WorkingAOTexture.ColorTexture );
			commands.Attributes.Set( "SpatialOut", AOTextureCurrent.ColorTexture );
			commands.Attributes.Set( "SpatialStep", 1 );
			commands.DispatchCompute( GtaoCs, AOTextureCurrent.Size );

			// Pass 2: current AO -> working AO (step 2)
			commands.ResourceBarrierTransition( AOTextureCurrent, ResourceState.NonPixelShaderResource );
			commands.ResourceBarrierTransition( WorkingAOTexture, ResourceState.UnorderedAccess );
			commands.Attributes.Set( "SpatialIn", AOTextureCurrent.ColorTexture );
			commands.Attributes.Set( "SpatialOut", WorkingAOTexture.ColorTexture );
			commands.Attributes.Set( "SpatialStep", 2 );
			commands.DispatchCompute( GtaoCs, AOTextureCurrent.Size );

			// Pass 3: working AO -> current AO (step 4)
			commands.ResourceBarrierTransition( WorkingAOTexture, ResourceState.NonPixelShaderResource );
			commands.ResourceBarrierTransition( AOTextureCurrent, ResourceState.UnorderedAccess );
			commands.Attributes.Set( "SpatialIn", WorkingAOTexture.ColorTexture );
			commands.Attributes.Set( "SpatialOut", AOTextureCurrent.ColorTexture );
			commands.Attributes.Set( "SpatialStep", 4 );
			commands.DispatchCompute( GtaoCs, AOTextureCurrent.Size );
		}

		//
		// Bilateral upsample to full resolution if running at reduced AO resolution.
		// Uses depth edge-stopping weights matched to the GTAO view-space depth
		// so the upsample doesn't bleed AO across depth discontinuities.
		//
		if ( scale > 1 )
		{
			commands.ResourceBarrierTransition( AOTextureCurrent, ResourceState.NonPixelShaderResource );

			RenderTargetHandle UpsampledAO = commands.GetRenderTarget( "UpsampledAO", ImageFormat.A8 );

			commands.Attributes.Set( "CoarseAO", AOTextureCurrent.ColorTexture );
			commands.Attributes.Set( "ViewDepth", ViewDepthChainTexture.ColorTexture );
			commands.Attributes.Set( "FullResAO", UpsampledAO.ColorTexture );

			commands.Attributes.SetCombo( "D_PASS", GTAOPasses.BilateralUpsample );
			commands.DispatchCompute( GtaoCs, UpsampledAO.Size );

			commands.ResourceBarrierTransition( UpsampledAO, ResourceState.PixelShaderResource );

			commands.GlobalAttributes.Set( "ScreenSpaceAmbientOcclusionTexture", UpsampledAO.ColorIndex );
		}
		else
		{
			commands.ResourceBarrierTransition( AOTextureCurrent, ResourceState.PixelShaderResource );

			commands.GlobalAttributes.Set( "ScreenSpaceAmbientOcclusionTexture", AOTextureCurrent.ColorIndex );
		}

		InsertCommandList( commands, Stage.AfterDepthPrepass, 0, "Ambient Occlusion" );
	}

}
