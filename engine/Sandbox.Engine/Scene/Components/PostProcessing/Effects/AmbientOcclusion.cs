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
	/// Run ambient occlusion at a reduced resolution to save GPU time.
	/// The AO texture is sampled with bilinear filtering when applied to the scene.
	/// </summary>
	[ConVar( "r_ao_resolution", Min = 1, Max = 8, Help = "Ambient occlusion resolution scale divisor (1: full, 2: half, 4: quarter, 8: eighth)" )]
	internal static int UserResolution { get; set; } = 2;

	/// <summary>
	/// Number of same-frame spatial (À-Trous) denoise passes. Each pass doubles the sample step (1, 2, 4).
	/// </summary>
	[ConVar( "r_ao_denoise_passes", Min = 1, Max = 3, Help = "Number of spatial (À-Trous) denoise passes (1-3)" )]
	internal static int UserDenoisePasses { get; set; } = 3;

	/// <summary>
	/// Ground truth for judging the real thing: this many slices at full resolution, 8 steps each, no denoising.
	/// </summary>
	[ConVar( "r_ao_reference", Min = 0, Max = 128, Help = "Debug: render AO at full resolution with this many slices (8 steps each) and no denoising, as a quality reference. 0 is off" )]
	internal static int ReferenceSlices { get; set; } = 0;

	/// <summary>
	/// Slightly reduce impact of samples further back to counter the bias from depth-based (incomplete) input scene geometry data
	/// </summary>
	[Property, Category( "Quality" ), Range( 0.0f, 5.0f )]
	public float ThinCompensation { get; set; } = 5.0f;

	/// <summary>
	/// Blue-noise texture used by GTAO sampling.
	/// </summary>
	Texture BlueNoise { get; set; } = Texture.Load( "textures/dev/blue_noise_256.vtex" );

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
		public float TAABlendAmount = 0;                 // unused — kept for cbuffer layout match with XeGTAO.h
		public float FinalValuePower = 2.2f;             // modifies the final ambient occlusion value using power function - this allows some of the above heuristics to do different things
		public float DenoiseBlurBeta = 1.5f;
		public float SampleDistributionPower = 2.0f;      // unused — the shader squares; kept for cbuffer layout match with XeGTAO.h
		public float ThinOccluderCompensation = 0.0f;    // the new 'thickness heuristic' approach
		public float DepthMIPSamplingOffset = 3.30f;     // main trade-off between performance (memory bandwidth) and quality (temporal stability is the first affected, thin objects next)
		public int NoiseIndex = 0;            // unused — kept for cbuffer layout match with XeGTAO.h
		public GTAOConstants() { }
	};

	enum GTAOPasses
	{
		ViewDepthChain,
		MainPass,
		DenoiseSpatial,
		BilateralUpsample
	}

	//-------------------------------------------------------------------------

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

		consts.ThinOccluderCompensation = ThinCompensation;

		// Map [0,1] intensity to a reasonable power curve.
		// pow(visibility, power) where power=0 gives no darkening, power=4 gives strong AO.
		consts.FinalValuePower = GetWeighted( x => x.Intensity, 1.0f ) * 4.0f;

		return consts;
	}

	CommandList commands = new CommandList( "Ambient Occlusion" );

	private static ComputeShader GtaoCs = new ComputeShader( "gtao_cs" );

	// A GPU profiler row per pass. The async compute queue has no timestamps, so +r_managed_async_compute 0 to see them
	static readonly ProfilingSampler DepthChainSampler = new( "AO Depth Chain" );
	static readonly ProfilingSampler MainPassSampler = new( "AO Main Pass" );
	static readonly ProfilingSampler DenoiseSampler = new( "AO Denoise" );
	static readonly ProfilingSampler UpsampleSampler = new( "AO Upsample" );

	/// <summary>
	/// Reads the normals and roughness G-buffer the depth-normals prepass writes (<c>NormalsGBuffer</c>), which the managed
	/// scene renderer draws only for cameras with an effect that needs it.
	/// </summary>
	internal override bool NeedsDepthNormals => true;

	// GTAO is compute dispatches and barriers from the depth chain and normals into its own targets
	internal override PostProcessQueue? EarlyQueue => PostProcessQueue.AsyncCompute;

	public override void Render()
	{
		if ( UserQuality <= 0 )
			return;

		commands.Reset();

		bool reference = ReferenceSlices > 0;
		int scale = reference ? 1 : UserResolution.Clamp( 1, 8 );

		// All at AO resolution: view depth with MIPs, and visibility twice to ping-pong through the denoiser
		RenderTargetHandle ViewDepthChainTexture = commands.GetRenderTarget( "ViewDepthChainTexture", ImageFormat.R16F, sizeFactor: scale, numMips: 5 );
		RenderTargetHandle AOZ0 = commands.GetRenderTarget( "AOZ0", ImageFormat.R16F, sizeFactor: scale );
		RenderTargetHandle AOZ1 = commands.GetRenderTarget( "AOZ1", ImageFormat.R16F, sizeFactor: scale );

		commands.Attributes.SetData( "GTAOConstants", GetGTAOConstants() );
		commands.Attributes.Set( "BlueNoise", BlueNoise );
		commands.Attributes.Set( "ReferenceSlices", ReferenceSlices );
		commands.Attributes.SetValue( "D_MSAA_NORMALS", RenderValue.MsaaCombo );
		commands.Attributes.SetCombo( "D_QUALITY", reference ? 3 : (UserQuality - 1).Clamp( 0, 2 ) );

		commands.Attributes.Set( "WorkingDepthMIP0", ViewDepthChainTexture.ColorTexture, 0 );
		commands.Attributes.Set( "WorkingDepthMIP1", ViewDepthChainTexture.ColorTexture, 1 );
		commands.Attributes.Set( "WorkingDepthMIP2", ViewDepthChainTexture.ColorTexture, 2 );
		commands.Attributes.Set( "WorkingDepthMIP3", ViewDepthChainTexture.ColorTexture, 3 );
		commands.Attributes.Set( "WorkingDepthMIP4", ViewDepthChainTexture.ColorTexture, 4 );
		commands.Attributes.Set( "WorkingDepth", ViewDepthChainTexture.ColorTexture );
		commands.Attributes.Set( "OutAOZ", AOZ0.ColorTexture );

		commands.Attributes.Set( "ResolutionScale", scale );

		// View depth chain: a thread for every 2x2 AO texels, rounded up so odd sizes get their last row and column
		using ( commands.ProfileScope( DepthChainSampler ) )
		{
			commands.Attributes.SetCombo( "D_PASS", GTAOPasses.ViewDepthChain );
			commands.DispatchCompute( GtaoCs, commands.ViewportSizeScaled( 2 * scale ) );
			commands.ResourceBarrierTransition( ViewDepthChainTexture, ResourceState.NonPixelShaderResource );
		}

		using ( commands.ProfileScope( MainPassSampler ) )
		{
			commands.Attributes.SetCombo( "D_PASS", GTAOPasses.MainPass );
			commands.DispatchCompute( GtaoCs, AOZ0.Size );
			commands.ResourceBarrierTransition( AOZ0, ResourceState.NonPixelShaderResource );
		}

		// Same-frame multi-pass spatial denoise with dilated steps; the reference is converged as it is
		var current = AOZ0;
		if ( !reference )
		{
			using var denoiseScope = commands.ProfileScope( DenoiseSampler );

			commands.Attributes.SetCombo( "D_PASS", GTAOPasses.DenoiseSpatial );

			int passes = UserDenoisePasses.Clamp( 1, 3 );
			var next = AOZ1;

			// Steps 1 and 2 share a dispatch, step 4 gets its own
			for ( int pass = 0; pass < passes; )
			{
				int steps = pass == 0 ? Math.Min( 2, passes ) : 1;

				commands.ResourceBarrierTransition( next, ResourceState.UnorderedAccess );
				commands.Attributes.Set( "AOZ", current.ColorTexture );
				commands.Attributes.Set( "OutDenoised", next.ColorTexture );
				commands.Attributes.Set( "SpatialStep", 1 << pass );
				commands.Attributes.Set( "SpatialSteps", steps );
				commands.DispatchCompute( GtaoCs, next.Size );
				commands.ResourceBarrierTransition( next, ResourceState.NonPixelShaderResource );

				var swap = current;
				current = next;
				next = swap;
				pass += steps;
			}
		}

		//
		// Bilateral upsample to full resolution if running at reduced AO resolution. Weighs the coarse texels around
		// each pixel by their tangent plane's distance to it, so AO doesn't bleed across depth discontinuities.
		//
		if ( scale > 1 )
		{
			using var upsampleScope = commands.ProfileScope( UpsampleSampler );

			RenderTargetHandle UpsampledAO = commands.GetRenderTarget( "UpsampledAO", ImageFormat.A8 );

			commands.Attributes.Set( "AOZ", current.ColorTexture );
			commands.Attributes.Set( "FullResAO", UpsampledAO.ColorTexture );

			commands.Attributes.SetCombo( "D_PASS", GTAOPasses.BilateralUpsample );
			commands.DispatchCompute( GtaoCs, UpsampledAO.Size );

			commands.ResourceBarrierTransition( UpsampledAO, ResourceState.PixelShaderResource );

			commands.SetPipelineTexture( PipelineTextureSlot.AmbientOcclusion, UpsampledAO.ColorTexture );
		}
		else
		{
			// Visibility is in red, which is what the forward shaders sample
			commands.ResourceBarrierTransition( current, ResourceState.PixelShaderResource );

			commands.SetPipelineTexture( PipelineTextureSlot.AmbientOcclusion, current.ColorTexture );
		}

		InsertCommandList( commands, Stage.AfterDepthPrepass, 0, "Ambient Occlusion" );
	}

}
