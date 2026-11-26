namespace Sandbox.Rendering;

/// <summary>
/// Screen-space reflections trace right after the depth prepass's Hi-Z chain (<see cref="Trace"/>) and keep the lit scene
/// after opaque as the next frame's history (<see cref="CaptureHistory"/>). Materials sample the result through
/// <c>DynamicReflections</c> using the <see cref="PipelineTextureSlot.Reflections"/> pipeline texture.
/// Each camera view owns its history.
/// </summary>
internal sealed class ScreenSpaceReflectionsPass : IDisposable
{
	[ConVar( "r_ssr_downsample_ratio", Help = "Screen-space reflection resolution scale (0 = Disabled, 1 = Full, 2 = Quarter, 4 = Sixteenth)." )]
	internal static int DownsampleRatio { get; set; } = 2;

	/// <summary>
	/// Whether the graphics settings leave screen-space reflections on.
	/// </summary>
	internal static bool IsEnabled => DownsampleRatio >= 1;

	// The trace lands in mip log2(ratio), so only powers of two that fit the chain line up
	static int Ratio => (int)Math.Min( System.Numerics.BitOperations.RoundUpToPowerOf2( (uint)DownsampleRatio ), 1u << (ReflectionMips - 1) );

	/// <summary>
	/// Stop tracing rays after this roughness value: very rough surfaces are unlikely to show any reflection.
	/// </summary>
	const float RoughnessCutoff = 0.5f;

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

	static readonly ComputeShader ShaderCs = new( "screen_space_reflections_cs" );
	static readonly ComputeShader ClassifyShaderCs = new( "screen_space_reflections_classify_cs" );

	static readonly ProfilingSampler[] PassProfiles =
	[
		new( "SSR classify tiles" ),
		new( "SSR intersect" ),
		new( "SSR bilateral upscale" )
	];

	static readonly GpuBuffer.IndirectDispatchArguments[] InitialDispatchArgs =
	[
		new() { ThreadGroupCountX = 0, ThreadGroupCountY = 1, ThreadGroupCountZ = 1 },
		new() { ThreadGroupCountX = 0, ThreadGroupCountY = 1, ThreadGroupCountZ = 1 }
	];

	readonly CommandList trace = new( "Screen-space reflections" );
	GpuBuffer<GpuBuffer.IndirectDispatchArguments> dispatchArgs;

	Texture historyColor;
	ImageFormat? historyFormat;
	bool hasHistory;
	BorealisGpuUpscaler.CutDetector cuts;

	/// <summary>
	/// Trace this view's reflections into the reflections pipeline texture. Needs the depth-normals G-buffer and the Hi-Z
	/// chain; on the graphics queue, since it clears. In a <see cref="Graphics"/> block on the render thread.
	/// </summary>
	internal void Trace( Vector3 position, Rotation rotation, object world, ImageFormat? colorFormat )
	{
		trace.Reset();

		var cut = cuts.Cut( position, rotation, world, Application.FrameCount );
		var size = Graphics.Viewport.Size;
		var lastFrame = hasHistory && !cut && historyColor.ImageFormat == colorFormat
			&& historyColor.Width == (int)size.x && historyColor.Height == (int)size.y ? historyColor : null;
		historyFormat = colorFormat;
		hasHistory = false;

		// The frame starts with the reflections slot empty. Leave it that way without usable history, so materials
		// fall back to their environment instead of treating black as a successful reflection.
		if ( lastFrame is null ) return;

		using ( Performance.Scope( "SSR record" ) ) Record( lastFrame );
		using ( Performance.Scope( "SSR trace" ) ) trace.ExecuteOnRenderThread();
	}

	/// <summary>
	/// Keep the lit scene as the next frame's history. After opaque, with the view's colour bound.
	/// </summary>
	internal void CaptureHistory()
	{
		if ( historyFormat is not { } format ) return;

		using var scope = Performance.Scope( "SSR history" );
		var viewport = Graphics.Viewport;
		int width = (int)viewport.Width, height = (int)viewport.Height;
		if ( historyColor is null || historyColor.Width != width || historyColor.Height != height || historyColor.ImageFormat != format )
		{
			// This is the view's own image, not a frame grab returned to the shared pool at the end of the block.
			var mips = Math.Min( 6, System.Numerics.BitOperations.Log2( (uint)Math.Min( width, height ) ) + 1 );
			var color = Texture.CreateRenderTarget().WithSize( width, height ).WithFormat( format ).WithMips( mips )
				.WithUAVBinding().Create( "SSR history" );
			historyColor?.Dispose();
			historyColor = color;
		}

		// Both stages record on the graphics queue, so this overwrites the image only after the trace has read it.
		Graphics.Backend.ResolveColor( historyColor, viewport );
		// The cone blur samples the mip matching its footprint, taps along the lobe hide the box filter
		Graphics.GenerateMipMaps( historyColor, Graphics.DownsampleMethod.GGX );
		hasHistory = true;
	}

	void Record( Texture lastFrame )
	{
		bool needsUpscale = Ratio != 1;
		dispatchArgs ??= new GpuBuffer<GpuBuffer.IndirectDispatchArguments>( 2, GpuBuffer.UsageFlags.Structured | GpuBuffer.UsageFlags.IndirectDrawArguments, "SSR_IntersectDispatchArgs" );
		// Recorded, so every execution of this list starts the tile counts from zero
		trace.SetBufferData( dispatchArgs, InitialDispatchArgs );

		// The trace writes straight into the mip matching its resolution (half res = mip 1), the upscale fills mip 0 from it
		int traceMip = System.Numerics.BitOperations.Log2( (uint)Ratio );
		var reflection = trace.GetRenderTarget( "SSR_Reflection", 1, ImageFormat.RGBA16161616F, ImageFormat.None, numMips: ReflectionMips );
		var guide = trace.GetRenderTarget( "SSR_Guide", Ratio, ImageFormat.RGBA16161616F, ImageFormat.None );
		// Classified tile list, sized from this view like guide: floor(V/4R) slots per axis cover its ceil(V/8R) tiles
		var tiles = trace.GetRenderTarget( "SSR_Tiles", Ratio * 4, ImageFormat.R32_UINT, ImageFormat.None );

		trace.Attributes.Set( "PreviousFrameColorIndex", lastFrame.Index );
		trace.Attributes.Set( "RoughnessCutoff", RoughnessCutoff );
		trace.Attributes.Set( "Scale", 1.0f / Ratio );
		trace.Attributes.Set( "ScaleInv", (float)Ratio );
		// Normals read the G-buffer bound directly, typed by this, not through bindless
		trace.Attributes.SetValue( "D_MSAA_NORMALS", RenderValue.MsaaCombo );

		trace.ResourceBarrierTransition( lastFrame, ResourceState.NonPixelShaderResource );

		// The indirect passes write only classified tiles; classify writes the zeros everything else reads
		trace.ResourceBarrierTransition( reflection, ResourceState.UnorderedAccess );
		trace.ResourceBarrierTransition( guide, ResourceState.UnorderedAccess );

		// Classify tiles
		using ( trace.ProfileScope( PassProfiles[0] ) )
		{
			trace.ResourceBarrierTransition( tiles, ResourceState.UnorderedAccess );
			trace.ResourceBarrierTransition( dispatchArgs, ResourceState.CopyDestination, ResourceState.UnorderedAccess );
			trace.Attributes.Set( "ClassifiedTilesRW", tiles.ColorTexture );
			trace.Attributes.Set( "IntersectDispatchArgsRW", dispatchArgs );
			trace.Attributes.Set( "OutRadiance", reflection.ColorTexture, traceMip );
			trace.Attributes.Set( "OutGuide", guide.ColorTexture );
			trace.Attributes.Set( "OutFull", reflection.ColorTexture, 0 );
			trace.DispatchCompute( ClassifyShaderCs, guide.Size );
			trace.UavBarrier( reflection );
			trace.UavBarrier( guide );
			trace.ResourceBarrierTransition( tiles, ResourceState.NonPixelShaderResource );
			trace.ResourceBarrierTransition( dispatchArgs, ResourceState.UnorderedAccess, ResourceState.IndirectArgument );
		}

		// Intersect
		using ( trace.ProfileScope( PassProfiles[(int)Passes.Intersect] ) )
		{
			trace.Attributes.Set( "ClassifiedTiles", tiles.ColorTexture );
			trace.Attributes.Set( "OutGuide", guide.ColorTexture );
			trace.Attributes.Set( "OutRadiance", reflection.ColorTexture, traceMip );
			trace.Attributes.SetCombo( "D_PASS", (int)Passes.Intersect );
			trace.DispatchComputeIndirect( ShaderCs, dispatchArgs, (int)DispatchArgsEntry.Intersect );
			trace.ResourceBarrierTransition( reflection, ResourceState.NonPixelShaderResource, traceMip );
		}

		if ( needsUpscale )
		{
			using var passScope = trace.ProfileScope( PassProfiles[(int)Passes.BilateralUpscale] );

			trace.ResourceBarrierTransition( guide, ResourceState.NonPixelShaderResource );
			trace.Attributes.Set( "Radiance", reflection.ColorTexture, traceMip );
			trace.Attributes.Set( "Guide", guide.ColorTexture );
			trace.Attributes.Set( "OutRadiance", reflection.ColorTexture, 0 );
			trace.Attributes.SetCombo( "D_PASS", (int)Passes.BilateralUpscale );
			trace.DispatchComputeIndirect( ShaderCs, dispatchArgs, (int)DispatchArgsEntry.BilateralUpscale );
			trace.UavBarrier( reflection );
		}

		// Rebuild from the bilateral-upscaled mip 0 so the raw trace doesn't bleed across edges
		trace.GenerateMipMaps( reflection, Graphics.DownsampleMethod.GGX );
		trace.PublishPipelineTexture( PipelineTextureSlot.Reflections, reflection.ColorTexture );
	}

	public void Dispose()
	{
		trace.Reset();
		hasHistory = false;
		historyColor?.Dispose();
		historyColor = null;
		dispatchArgs?.Dispose();
		dispatchArgs = null;
	}
}
