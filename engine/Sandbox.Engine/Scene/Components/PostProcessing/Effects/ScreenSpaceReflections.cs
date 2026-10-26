using Sandbox.Rendering;

namespace Sandbox;

[Title( "Screen-Space Reflections" )]
[Category( "Post Processing" )]
[Icon( "local_mall" )]
public class ScreenSpaceReflections : BasePostProcess<ScreenSpaceReflections>
{
	[ConVar( "r_ssr_downsample_ratio", Help = "Default SSR resolution scale (0 = Disabled, 1 = Full, 2 = Quarter, 4 = Sixteeneth)." )]
	internal static int DownsampleRatio { get; set; } = 2;

	// The trace lands in mip log2(ratio), so only powers of two that fit the chain line up
	static int Ratio => (int)Math.Min( System.Numerics.BitOperations.RoundUpToPowerOf2( (uint)DownsampleRatio ), 1u << (ReflectionMips - 1) );

	/// <summary>
	/// Stop tracing rays after this roughness value.
	/// This is meant to be used to avoid tracing rays for very rough surfaces which are unlikely to have any reflections.
	/// This is a performance optimization.
	/// </summary>
	public float RoughnessCutoff => 0.5f;

	/// <summary>
	/// Compositing samples mip roughness² * 4 of the result, so rougher surfaces get a blurrier reflection
	/// </summary>
	const int ReflectionMips = 5;

	enum Passes
	{
		Intersect = 1,
		BilateralUpscale = 2
	}

	enum DispatchArgsEntry
	{
		Intersect = 0,
		BilateralUpscale = 1
	}

	CommandList cmd = new CommandList( "ScreenSpaceReflections" );
	CommandList cmdLastframe = new CommandList( "ScreenSpaceReflections (Last Frame)" );

	private static ComputeShader ShaderCs = new ComputeShader( "screen_space_reflections_cs" );
	private static ComputeShader ClassifyShaderCs = new ComputeShader( "screen_space_reflections_classify_cs" );

	static readonly ProfilingSampler[] PassProfiles =
	[
		new( "SSR classify tiles" ),
		new( "SSR intersect" ),
		new( "SSR bilateral upscale" )
	];

	private GpuBuffer<GpuBuffer.IndirectDispatchArguments> DispatchArgsBuffer;

	private static readonly GpuBuffer.IndirectDispatchArguments[] InitialDispatchArgs =
	[
		new() { ThreadGroupCountX = 0, ThreadGroupCountY = 1, ThreadGroupCountZ = 1 },
		new() { ThreadGroupCountX = 0, ThreadGroupCountY = 1, ThreadGroupCountZ = 1 }
	];

	protected override void OnEnabled()
	{
		base.OnEnabled();

		cmdLastframe.Reset();
		// The cone blur samples the mip matching its footprint, taps along the lobe hide the box filter
		cmdLastframe.Attributes.GrabFrameTexture( "LastFrameColor", Graphics.DownsampleMethod.GGX, maxMips: 6 );
	}

	protected override void OnDisabled()
	{
		base.OnDisabled();

		DispatchArgsBuffer?.Dispose();
		DispatchArgsBuffer = null;
	}

	/// <summary>
	/// Reads the normals and roughness G-buffer the depth-normals prepass writes (<c>NormalsGBuffer</c>).
	/// </summary>
	internal override bool NeedsDepthNormals => true;

	// Reads prepass depth/normals and its own history, never AO or contact shadows. Clears require the graphics queue.
	internal override PostProcessQueue? EarlyQueue => PostProcessQueue.Graphics;

	public override void Render()
	{
		cmd.Reset();

		if ( DownsampleRatio < 1 )
			return;

		bool needsUpscale = Ratio != 1;
		var lastFrame = cmdLastframe.Attributes.GetRenderTarget( "LastFrameColor" )?.ColorTarget ?? Texture.Transparent;
		DispatchArgsBuffer ??= new GpuBuffer<GpuBuffer.IndirectDispatchArguments>( 2, GpuBuffer.UsageFlags.Structured | GpuBuffer.UsageFlags.IndirectDrawArguments, "SSR_IntersectDispatchArgs" );
		// Recorded, so every execution of this list starts the tile counts from zero
		cmd.SetBufferData( DispatchArgsBuffer, InitialDispatchArgs );

		// The trace writes straight into the mip matching its resolution (half res = mip 1), the upscale fills mip 0 from it
		int traceMip = System.Numerics.BitOperations.Log2( (uint)Ratio );
		var reflection = cmd.GetRenderTarget( "SSR_Reflection", 1, ImageFormat.RGBA16161616F, ImageFormat.None, numMips: ReflectionMips );
		var guide = cmd.GetRenderTarget( "SSR_Guide", Ratio, ImageFormat.RGBA16161616F, ImageFormat.None );
		// Classified tile list, sized from this view like guide: floor(V/4R) slots per axis cover its ceil(V/8R) tiles
		var tiles = cmd.GetRenderTarget( "SSR_Tiles", Ratio * 4, ImageFormat.R32_UINT, ImageFormat.None );

		cmd.Attributes.Set( "PreviousFrameColorIndex", lastFrame.Index );
		cmd.Attributes.Set( "RoughnessCutoff", RoughnessCutoff );
		cmd.Attributes.Set( "Scale", 1.0f / Ratio );
		cmd.Attributes.Set( "ScaleInv", (float)Ratio );

		cmd.ResourceBarrierTransition( lastFrame, ResourceState.NonPixelShaderResource );

		// Clear since we are indirect
		cmd.Clear( reflection, Color.Transparent );
		cmd.Clear( guide, Color.Transparent );

		// Classify tiles
		using ( cmd.ProfileScope( PassProfiles[0] ) )
		{
			cmd.ResourceBarrierTransition( tiles, ResourceState.UnorderedAccess );
			cmd.ResourceBarrierTransition( DispatchArgsBuffer, ResourceState.CopyDestination, ResourceState.UnorderedAccess );
			cmd.Attributes.Set( "ClassifiedTilesRW", tiles.ColorTexture );
			cmd.Attributes.Set( "IntersectDispatchArgsRW", DispatchArgsBuffer );
			cmd.Attributes.Set( "OutRadiance", reflection.ColorTexture, traceMip );
			cmd.DispatchCompute( ClassifyShaderCs, guide.Size );
			cmd.UavBarrier( reflection );
			cmd.ResourceBarrierTransition( tiles, ResourceState.NonPixelShaderResource );
			cmd.ResourceBarrierTransition( DispatchArgsBuffer, ResourceState.UnorderedAccess, ResourceState.IndirectArgument );
		}

		// Intersect
		using ( cmd.ProfileScope( PassProfiles[(int)Passes.Intersect] ) )
		{
			cmd.Attributes.Set( "ClassifiedTiles", tiles.ColorTexture );
			cmd.Attributes.Set( "OutGuide", guide.ColorTexture );
			cmd.Attributes.SetCombo( "D_PASS", (int)Passes.Intersect );
			cmd.DispatchComputeIndirect( ShaderCs, DispatchArgsBuffer, (int)DispatchArgsEntry.Intersect );
			cmd.ResourceBarrierTransition( reflection, ResourceState.NonPixelShaderResource, traceMip );
		}

		if ( needsUpscale )
		{
			using var passScope = cmd.ProfileScope( PassProfiles[(int)Passes.BilateralUpscale] );

			cmd.ResourceBarrierTransition( guide, ResourceState.NonPixelShaderResource );
			cmd.Attributes.Set( "Radiance", reflection.ColorTexture, traceMip );
			cmd.Attributes.Set( "Guide", guide.ColorTexture );
			cmd.Attributes.Set( "OutRadiance", reflection.ColorTexture, 0 );
			cmd.Attributes.SetCombo( "D_PASS", (int)Passes.BilateralUpscale );
			cmd.DispatchComputeIndirect( ShaderCs, DispatchArgsBuffer, (int)DispatchArgsEntry.BilateralUpscale );
			cmd.UavBarrier( reflection );
		}

		// Rebuild from the bilateral-upscaled mip 0 so the raw trace doesn't bleed across edges
		cmd.GenerateMipMaps( reflection, Graphics.DownsampleMethod.GGX );
		cmd.ResourceBarrierTransition( reflection, ResourceState.PixelShaderResource );
		cmd.SetPipelineTexture( PipelineTextureSlot.Reflections, reflection.ColorTexture );

		InsertCommandList( cmdLastframe, Stage.AfterOpaque, 0, "ScreenSpaceReflections" );
		InsertCommandList( cmd, Stage.AfterDepthPrepass, int.MaxValue, "ScreenSpaceReflections" );
	}
}
