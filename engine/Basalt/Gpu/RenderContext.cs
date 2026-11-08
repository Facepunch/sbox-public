using NativeEngine;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Basalt.Gpu;

/// <summary>
/// Native RHI recording and interop boundary.
/// Frame contexts handle setup; segment contexts record passes (<c>CRenderBatchList</c>).
/// Attributes form frame, segment and draw levels, keeping concurrent writes isolated.
/// </summary>
internal sealed class RenderContext : IDisposable
{
	IRenderContext context;
	CRenderAttributes nativeAttributes;
	CRenderAttributes drawAttributes;
	bool drawAttributesMerged;

	// Re-merge after engine drawing, which may mutate attributes in place.
	ShaderParameters mergedObject, mergedLightmap;
	bool mergedValid;
	bool borrowed;
	RenderContext frame;
	bool debugMarkers;

	internal bool GpuScopesEnabled => RenderHost.Current.GpuTimings || debugMarkers;

	/// <summary>
	/// A frame's own context.
	/// </summary>
	public RenderContext()
	{
	}

	/// <summary>
	/// Create a segment context whose attributes override the frame's.
	/// </summary>
	public RenderContext( RenderContext frame )
	{
		ArgumentNullException.ThrowIfNull( frame );
		this.frame = frame;
	}

	/// <summary>
	/// Record a frame segment into its own or a shared native context.
	/// </summary>
	public void BeginSegment( RenderContext frame, RenderContext shared )
	{
		if ( this.frame is null || frame is null ) throw new InvalidOperationException( "Only a segment context records a frame's segments" );
		this.frame = frame;
		Begin( shared );
	}

	/// <summary>
	/// Record on the async compute queue (<c>RCFLAG_ASYNC_COMPUTE_QUEUE</c>): compute only. Set before <see cref="Begin"/>.
	/// </summary>
	public bool AsyncCompute { get; set; }

	const uint AsyncComputeQueue = 0x0008;

	/// <summary>
	/// Whether the device has an async compute queue: a queue family for compute apart from graphics, or a second graphics
	/// queue (native's <c>SupportsAsyncCompute</c>). Not under RenderDoc, which offers one queue.
	/// </summary>
	public static bool SupportsAsyncCompute => supportsAsyncCompute ??= RenderDeviceManager.SupportsAsyncCompute();
	static bool? supportsAsyncCompute;

	/// <summary>
	/// Signal a semaphore when this context's work is done, for another to <see cref="WaitAtBegin"/>. Once per context,
	/// after recording.
	/// </summary>
	public RenderSemaphoreHandle_t SignalAtEnd() => context.SemaphoreSignalAtEnd();

	/// <summary>
	/// Start this context's work only once another's <see cref="SignalAtEnd"/> has signalled. Once per context, before it's
	/// submitted.
	/// </summary>
	public void WaitAtBegin( RenderSemaphoreHandle_t semaphore ) => context.SemaphoreWaitAtBegin( semaphore );

	/// <summary>
	/// Whether this is the frame's setup context.
	/// </summary>
	internal bool IsFrame => frame is null;

	/// <summary>
	/// View constants and pass attributes, created on first <see cref="Begin"/> to allow engine-free construction.
	/// </summary>
	internal ShaderParameters Attributes { get; private set; }

	/// <summary>
	/// Cached native material bind for similar-material rebinds (<c>RenderTools.DrawModelDrawCall</c>).
	/// Invalidate whenever draw inputs change.
	/// </summary>
	IntPtr materialBind;

	/// <summary>
	/// Force a full material bind after external attribute changes or drawing.
	/// </summary>
	public void ResetMaterialBind()
	{
		if ( materialBind != IntPtr.Zero ) RenderTools.ResetMaterialBind( materialBind );
	}

	/// <summary>
	/// Full and similar-material binds this context's draws have made, ever (<c>RenderTools.GetMaterialBindCounts</c>). A
	/// range of draws made the difference.
	/// </summary>
	internal unsafe (int Full, int Similar) MaterialBinds
	{
		get
		{
			if ( materialBind == IntPtr.Zero ) return default;
			int full, similar;
			RenderTools.GetMaterialBindCounts( materialBind, (IntPtr)(&full), (IntPtr)(&similar) );
			return (full, similar);
		}
	}

	static unsafe IntPtr AllocMaterialBind() => (IntPtr)NativeMemory.AllocZeroed( (nuint)RenderTools.GetMaterialBindSize() );

	public unsafe void Dispose()
	{
		if ( materialBind != IntPtr.Zero )
		{
			NativeMemory.Free( (void*)materialBind );
			materialBind = IntPtr.Zero;
		}

		if ( nativeAttributes.IsNull ) return;

		drawAttributes.Clear( true, true );
		drawAttributes.DeleteThis();
		drawAttributes = default;
		nativeAttributes.Clear( true, true );
		nativeAttributes.DeleteThis();
		nativeAttributes = default;
		Attributes = null;
	}

	internal bool IsRecording => !context.IsNull;

	/// <summary>
	/// Native context for engine drawing APIs.
	/// </summary>
	internal IRenderContext Native => context;

	/// <summary>
	/// This thread's active segment context for Graphics calls.
	/// </summary>
	[ThreadStatic] internal static RenderContext Recording;

	/// <summary>
	/// Last targets, viewport and depth range; restored after custom drawing.
	/// </summary>
	internal StageTarget Bound { get; private set; }

	/// <summary>
	/// Whether cached target bindings remain valid.
	/// </summary>
	bool boundValid;

	/// <summary>
	/// Bind targets, viewport and depth range. Reuse unchanged bindings to keep adjacent passes in one native render pass.
	/// </summary>
	public void Bind( in StageTarget target )
	{
		if ( boundValid && SameTargets( Bound, target ) )
		{
			if ( Bound.Viewport != target.Viewport || Bound.MinZ != target.MinZ || Bound.MaxZ != target.MaxZ ) SetViewport( target.Viewport, target.MinZ, target.MaxZ );
			Bound = target;
			return;
		}

		ResetMaterialBind();
		if ( target.Color.IsNull ) RenderTools.BindDepthTarget( context, target.Depth, target.DepthSlice );
		else RenderTools.BindColorAndDepthTarget( context, target.Color, target.Depth, target.SrgbWrite );
		boundValid = true;

		Bound = target;
		SetViewport( target.Viewport, target.MinZ, target.MaxZ );
	}

	static bool SameTargets( in StageTarget a, in StageTarget b )
	{
		return a.Color.Equals( b.Color ) && a.Depth.Equals( b.Depth ) && a.DepthSlice == b.DepthSlice && a.SrgbWrite == b.SrgbWrite;
	}

	/// <summary>
	/// Restore cached targets and viewport after engine drawing.
	/// </summary>
	internal void RestoreBound()
	{
		Invalidate();
		Bind( Bound );
	}

	/// <summary>
	/// Invalidate target, attribute and material caches after external drawing.
	/// </summary>
	internal void Invalidate()
	{
		boundValid = false;
		mergedValid = false;
		ResetMaterialBind();
	}

	static readonly StringToken ViewConstantsName = new( "PerViewConstantBuffer_t" );

	/// <summary>
	/// Current view-constant key; null for unknown or unkeyed shadow constants.
	/// </summary>
	internal ViewConstantsKey? ViewConstants { get; private set; }

	/// <summary>
	/// Bind <c>PerViewConstantBuffer_t</c> and cache its key.
	/// </summary>
	public void SetViewConstants( in ViewConstants constants, ViewConstantsKey? key )
	{
		SetConstants( ViewConstantsName, constants );
		ViewConstants = key;
	}

	/// <summary>
	/// Cache the frame constants inherited by this segment.
	/// </summary>
	internal void InheritViewConstants( ViewConstantsKey key ) => ViewConstants = key;

	/// <summary>
	/// Set a combo and invalidate the material bind.
	/// </summary>
	public void SetCombo( StringToken name, int value )
	{
		Attributes.SetCombo( name, value );
		ResetMaterialBind();
	}

	/// <summary>
	/// Set a texture and invalidate the material bind.
	/// </summary>
	public void Set( StringToken name, GpuTexture value )
	{
		Attributes.Set( name, value );
		ResetMaterialBind();
	}

	// Pass-local values reset to zero at EndPass.
	readonly (StringToken Name, bool Combo)[] passState = new (StringToken, bool)[8];
	int passStateCount;

	/// <summary>
	/// Set a pass-local combo, reset to zero by <see cref="EndPass"/>.
	/// </summary>
	public void SetPassCombo( StringToken name, int value )
	{
		RememberPassState( name, combo: true );
		SetCombo( name, value );
	}

	/// <summary>
	/// Set a pass-local attribute, reset to zero by <see cref="EndPass"/>.
	/// </summary>
	public void SetPassAttribute( StringToken name, int value )
	{
		RememberPassState( name, combo: false );
		Attributes.Set( name, value );
		ResetMaterialBind();
	}

	void RememberPassState( StringToken name, bool combo )
	{
		for ( int i = 0; i < passStateCount; i++ )
		{
			if ( passState[i].Name.Value == name.Value ) return;
		}

		if ( passStateCount == passState.Length ) throw new InvalidOperationException( "Too many pass combos and attributes" );
		passState[passStateCount++] = (name, combo);
	}

	/// <summary>
	/// Reset pass-local values and overlay stencil, then invalidate the material bind.
	/// </summary>
	public void EndPass()
	{
		for ( int i = 0; i < passStateCount; i++ )
		{
			var (name, combo) = passState[i];
			if ( combo ) Attributes.SetCombo( name, 0 );
			else Attributes.Set( name, 0 );
		}

		passStateCount = 0;
		OverlayStencil = OverlayStencil.None;
		ResetMaterialBind();
	}

	/// <summary>
	/// Start an explicitly bounded GPU pass on the current graphics or compute context.
	/// </summary>
	public GpuScope BeginGpuScope( string name )
	{
		if ( !GpuScopesEnabled )
		{
			return default;
		}

		// Native takes the name as UTF-8, which a sampler holds; one per name, kept, since layers record on several threads
		var sampler = gpuScopeNames.GetOrAdd( name, static n => new Sandbox.Rendering.ProfilingSampler( n ) );
		var pix = debugMarkers;
		if ( pix )
		{
			context.BeginPixEvent( sampler.NamePtr );
		}

		var marker = RenderHost.Current.GpuTimings
			? CSceneSystem.BeginManagedPerfMarker( context, sampler.NamePtr, true, true )
			: IntPtr.Zero;
		return new( marker, pix );
	}

	public readonly record struct GpuScope( IntPtr Marker, bool Pix );

	static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Sandbox.Rendering.ProfilingSampler> gpuScopeNames = new();

	/// <summary>
	/// End a scope <see cref="BeginGpuScope"/> started.
	/// </summary>
	public void EndGpuScope( GpuScope scope )
	{
		if ( scope.Marker != IntPtr.Zero )
		{
			CSceneSystem.EndManagedPerfMarker( context, scope.Marker );
		}

		if ( scope.Pix )
		{
			context.EndPixEvent();
		}
	}

	/// <summary>
	/// A swap chain's colour buffer, as a texture to bind.
	/// </summary>
	public ITexture SwapChainColor( SwapChainHandle_t swapChain ) => g_pRenderDevice.GetSwapChainTexture( swapChain, SwapChainBuffer.BufferColor );

	/// <summary>
	/// Merge lightmap then object attributes above context attributes (<c>CBaseSceneObjectDesc::DrawArray</c>).
	/// </summary>
	public void SetObjectAttributes( ShaderParameters objectAttributes, ShaderParameters lightmap = null )
	{
		// Shared attributes preserve the material bind (CBaseSceneObjectDesc::DrawArray).
		if ( mergedValid && ReferenceEquals( objectAttributes, mergedObject ) && ReferenceEquals( lightmap, mergedLightmap ) ) return;
		mergedObject = objectAttributes;
		mergedLightmap = lightmap;
		mergedValid = true;

		// Changed attributes may alter combos or state.
		ResetMaterialBind();

		if ( drawAttributesMerged )
		{
			drawAttributes.Clear( false, false );
			drawAttributesMerged = false;
		}

		if ( lightmap is not null )
		{
			lightmap.Engine().Get().MergeToPtr( drawAttributes );
			drawAttributesMerged = true;
		}

		if ( objectAttributes is not null )
		{
			objectAttributes.Engine().Get().MergeToPtr( drawAttributes );
			drawAttributesMerged = true;
		}
	}







	/// <summary>
	/// Allocate native vertex-cache space and return the first vertex.
	/// </summary>
	public static int AllocateVertexCache( int vertices ) => RenderTools.AllocateVertexCache( vertices );

	/// <summary>
	/// Compute-skin a model mesh's instances into native vertex-cache blocks (<c>skinning_cs</c>).
	/// Optional per-instance volume ranges enable deformation; dispatch deformed and undeformed instances separately. Deformed
	/// instances read their anchors, per mesh bone, from their offset into <paramref name="anchors"/> - 0 for none.
	/// </summary>
	public unsafe bool DispatchSkinning( GpuMaterial skinningShader, RenderMesh mesh, int modelMesh, GpuBuffer transforms, ReadOnlySpan<int> slots, ReadOnlySpan<int> vertexCacheOffsets, int blendWeights,
		GpuBuffer volumes = null, ReadOnlySpan<int> volumeOffsets = default, ReadOnlySpan<int> volumeCounts = default, GpuBuffer anchors = null, ReadOnlySpan<int> anchorOffsets = default, bool morph = false )
	{
		fixed ( int* s = slots )
		fixed ( int* o = vertexCacheOffsets )
		fixed ( int* vo = volumeOffsets )
		fixed ( int* vc = volumeCounts )
		fixed ( int* ao = anchorOffsets )
		{
			var deformed = volumes is not null && anchors is not null;
			RenderBufferHandle_t volumeBuffer = deformed ? volumes.Native() : IntPtr.Zero;
			RenderBufferHandle_t anchorBuffer = deformed ? anchors.Native() : IntPtr.Zero;
			return RenderTools.DispatchModelMeshSkinning( context, skinningShader.Native().GetMode(), mesh.Geometry.Native(), modelMesh, transforms.Native(), (IntPtr)s, (IntPtr)o, slots.Length, blendWeights,
				volumeBuffer, deformed ? (IntPtr)vo : IntPtr.Zero, deformed ? (IntPtr)vc : IntPtr.Zero, anchorBuffer, deformed ? (IntPtr)ao : IntPtr.Zero, morph );
		}
	}


	/// <summary>
	/// Record queued morphs into this setup context, before skinning, sharing the frame's descriptor set.
	/// </summary>
	public void GenerateMorphs()
	{
		RenderTools.GenerateMorphs( context );
		boundValid = false;
		ResetMaterialBind();
	}

	/// <summary>
	/// Where a queued morph is in the atlas, once generated: u offset, u range, v offset, v range, for the mesh's morph entry.
	/// </summary>
	public static unsafe Vector4 MorphSubrect( int handle )
	{
		Vector4 subrect = default;
		RenderTools.GetMorphSubrect( handle, (IntPtr)(&subrect) );
		return subrect;
	}

	/// <summary>
	/// Before skinning: vertex shaders from earlier work that read the cache finish before compute writes it.
	/// </summary>
	public void BarrierVertexCacheToWrite()
	{
		context.BufferBarrierTransition( RenderTools.GetVertexCacheBuffer(), RenderBarrierPipelineStageFlags_t.PreRasterizationShadersBit, RenderBarrierPipelineStageFlags_t.ComputeShaderBit,
			RenderBarrierAccessFlags_t.ShaderReadBit, RenderBarrierAccessFlags_t.ShaderWriteBit );
	}

	/// <summary>
	/// After skinning: the writes are visible to the vertex shaders that read the cache.
	/// </summary>
	public void BarrierVertexCacheToRead()
	{
		context.BufferBarrierTransition( RenderTools.GetVertexCacheBuffer(), RenderBarrierPipelineStageFlags_t.ComputeShaderBit, RenderBarrierPipelineStageFlags_t.PreRasterizationShadersBit,
			RenderBarrierAccessFlags_t.ShaderWriteBit, RenderBarrierAccessFlags_t.ShaderReadBit );
	}

	/// <summary>
	/// Bind vertex-cache ID streams starting at stream 2, after instance IDs.
	/// </summary>
	public bool BindVertexCacheIds( RenderMesh mesh, in RenderMesh.Draw draw )
	{
		return RenderTools.BindModelDrawCallVertexIds( context, mesh.Geometry.Native(), draw.Mesh, draw.DrawCall, 2 );
	}

	/// <summary>
	/// Draw a <c>SceneSkyBox</c> into the bound target using view attributes.
	/// </summary>
	public bool DrawSky( SceneLighting lighting, StringToken shaderMode )
	{
		ResetMaterialBind();
		var material = lighting.SkyMaterial;
		var mode = material.Native().GetMode( shaderMode );
		if ( mode.IsNull ) mode = material.Native().GetMode();
		if ( mode.IsNull ) return false;

		var fog = lighting.SkyFog;
		var tint = lighting.SkyTint;
		return RenderTools.DrawSkyBox( context, Attributes.Engine().Get(), mode, new Transform( Vector3.Zero, lighting.SkyRotation ), new Vector4( tint.r, tint.g, tint.b, tint.a ),
			(int)fog.Type, fog.MinStart, fog.MinEnd, fog.MaxStart, fog.MaxEnd );
	}

	/// <summary>
	/// Whether a texture is loaded and has GPU data.
	/// </summary>
	public static bool HasData( GpuTexture texture ) => texture is { IsValid: true };

	/// <summary>
	/// Bindless sRGB view index using the texture's native dimension (<c>GetTextureViewIndex</c>).
	/// </summary>
	public static uint BindlessIndex( GpuTexture texture )
	{
		return (uint)texture.ViewIndex( true, TextureViewDimension.Default );
	}

	/// <summary>
	/// Begin recording into an owned or shared native context. Only the owner submits a shared context.
	/// </summary>
	public void Begin( RenderContext shared = null )
	{
		// Frame setup is main-thread-only; segments begin on their recording thread.
		if ( frame is null ) ThreadSafe.AssertIsMainThread();
		if ( IsRecording ) throw new InvalidOperationException( "Already recording" );

		if ( Attributes is null )
		{
			// Dispose explicitly before device shutdown; finalization may be too late.
			nativeAttributes = CRenderAttributes.Create();
			Attributes = Source2Parameters.From( new RenderAttributes( nativeAttributes ) );
			drawAttributes = CRenderAttributes.Create();
			drawAttributes.SetParent( nativeAttributes );
		}

		if ( frame is not null )
		{
			// Clear old pass state and inherit this frame's attributes.
			nativeAttributes.Clear( false, true );
			nativeAttributes.SetParent( frame.nativeAttributes );
			InstanceIds = frame.InstanceIds;
			Occlusion = frame.Occlusion;
		}

		drawAttributes.Clear( false, false );
		drawAttributesMerged = false;
		mergedObject = null;
		mergedLightmap = null;
		mergedValid = true;
		OverlayStencil = OverlayStencil.None;

		// Reset recording-local state.
		boundValid = false;
		Bound = default;
		ViewConstants = null;
		passStateCount = 0;

		// Material binds cannot survive across frames. Zeroed, so its bind counts start at nothing.
		if ( materialBind == IntPtr.Zero ) materialBind = AllocMaterialBind();
		ResetMaterialBind();

		borrowed = shared is not null;
		if ( borrowed && AsyncCompute ) throw new InvalidOperationException( "An async compute context records into its own native context" );
		context = borrowed ? shared.context : g_pRenderDevice.CreateRenderContext( AsyncCompute ? AsyncComputeQueue : 0 );
		debugMarkers = borrowed ? shared.debugMarkers : g_pRenderDevice.AreDebugMarkersEnabled();

		// The frame's bindless texture set, as CSceneSystem::InitializeRenderAttributes does for a view
		if ( frame is null ) Attributes.Engine().Get().SetGlobalBindlessDescriptorSet();

		// View constants resolve through context attributes, not draw parents (CRenderBatchList::Start).
		context.GetAttributesPtrForModify().SetParent( nativeAttributes );
	}

	/// <summary>
	/// Submit and end recording. Name the swap chain only on the first submission to include all work in GPU timing.
	/// </summary>
	public void Submit( SwapChainHandle_t swapChain = default )
	{
		if ( !IsRecording ) return;

		try
		{
			BeginSubmitting( swapChain );
			context.Submit();
		}
		finally
		{
			End();
		}
	}

	/// <summary>
	/// Submit after earlier work and end recording.
	/// </summary>
	public void SubmitNext()
	{
		if ( !IsRecording ) return;

		try
		{
			if ( !borrowed ) context.Submit();
		}
		finally
		{
			End();
		}
	}

	/// <summary>
	/// Start a frame's submission, naming the swap chain it ends up in for its GPU timing.
	/// </summary>
	public static void BeginSubmitting( SwapChainHandle_t swapChain ) => g_pRenderDevice.BeginSubmittingDisplayLists( swapChain );

	/// <summary>
	/// Native shader time, latched from scene <c>Time.Now</c> when view rendering begins.
	/// </summary>
	public static float RenderTime => CSceneSystem.GetCurrentRenderTime();

	/// <summary>
	/// Stop recording without submitting - what was recorded is dropped.
	/// </summary>
	public void End()
	{
		if ( !IsRecording ) return;

		if ( !borrowed ) g_pRenderDevice.ReleaseRenderContext( context );
		context = default;
		borrowed = false;
	}

	/// <summary>
	/// Copy HDR to the output, encoding 8-bit targets as sRGB (<c>ResolveHDRToFinalSDR</c>).
	/// Blending must happen before this copy to avoid clipping bright translucency.
	/// </summary>
	public void CopyToSwapChain( in RenderOutput output, Rect viewport )
	{
		if ( !output.HasCopy ) return;

		Invalidate();
		if ( output.Texture is { } texture ) RenderTools.BindColorAndDepthTarget( context, (output.Scratch ?? texture).Native(), default, !texture.Format.IsFloat() );
		else context.BindRenderTargets( output.SwapChain, true, false );

		RenderTools.BlitTexture( context, output.Target.Resolved.Native(), (int)viewport.Left, (int)viewport.Top, (int)viewport.Width, (int)viewport.Height, 0 );
	}

	/// <summary>
	/// Final colour target and its sRGB-write requirement.
	/// </summary>
	public ITexture OutputColor( in RenderOutput output, out bool srgbWrite )
	{
		if ( output.Texture is { } texture )
		{
			srgbWrite = !texture.Format.IsFloat();
			return (output.Scratch ?? texture).Native();
		}

		if ( (IntPtr)output.SwapChain != IntPtr.Zero )
		{
			srgbWrite = true;
			return SwapChainColor( output.SwapChain );
		}

		srgbWrite = output.Target.SrgbWrite;
		return output.Target.Samples > 1 ? output.Target.Resolved.Native() : output.Target.Color.Native();
	}

	/// <summary>
	/// Resolve bound MSAA colour into <see cref="ViewTarget.Resolved"/> (<c>CSceneSystem::ResolveLayer</c>).
	/// </summary>
	public void Resolve( in RenderOutput output )
	{
		if ( output.Target is not { Samples: > 1 } target ) return;

		var rect = new NativeRect( 0, 0, target.Size.x, target.Size.y );
		RenderTools.ResolveFrameBuffer( context, target.Resolved.Native(), rect );
	}

	/// <summary>
	/// Resolve a multisampled texture output's <see cref="RenderOutput.Scratch"/> into the texture, once everything has drawn.
	/// </summary>
	public void ResolveScratch( in RenderOutput output )
	{
		if ( output.Scratch is not { } scratch || output.Texture is not { } texture ) return;

		Invalidate();
		RenderTools.BindColorAndDepthTarget( context, scratch.Native(), default, !texture.Format.IsFloat() );
		RenderTools.ResolveFrameBuffer( context, texture.Native(), new NativeRect( 0, 0, texture.Width, texture.Height ) );
	}

	/// <summary>
	/// Copy the first array slices or cube faces between equal-sized textures.
	/// </summary>
	public void CopyTexture( GpuTexture source, GpuTexture destination, int slices )
	{
		var rect = new NativeRect( 0, 0, source.Width, source.Height );
		for ( uint slice = 0; slice < slices; slice++ )
			RenderTools.CopyTexture( context, source.Native(), destination.Native(), rect, 0, 0, 0, slice, 0, slice );
	}

	/// <summary>
	/// Transition depth to writable after prior pixel-shader reads.
	/// </summary>
	public void BarrierToDepthWrite( GpuTexture depth )
	{
		context.TextureBarrierTransition( depth.Native(), -1, RenderBarrierPipelineStageFlags_t.FragmentShaderBit,
			RenderBarrierPipelineStageFlags_t.EarlyFragmentTestsBit | RenderBarrierPipelineStageFlags_t.LateFragmentTestsBit,
			RenderImageLayout_t.RENDER_IMAGE_LAYOUT_DEPTH_STENCIL_ATTACHMENT_OPTIMAL, 0,
			RenderBarrierAccessFlags_t.DepthStencilAttachmentReadBit | RenderBarrierAccessFlags_t.DepthStencilAttachmentWriteBit );
	}

	/// <summary>
	/// Make a depth texture that was just rendered readable by compute on the async queue, in the layout a compute bind reads it
	/// in, so the compute queue has no transition of its own to make.
	/// </summary>
	public void BarrierToComputeRead( GpuTexture depth )
	{
		context.TextureBarrierTransition( depth.Native(), -1, RenderBarrierPipelineStageFlags_t.LateFragmentTestsBit,
			RenderBarrierPipelineStageFlags_t.ComputeShaderBit, RenderImageLayout_t.RENDER_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL,
			RenderBarrierAccessFlags_t.DepthStencilAttachmentWriteBit, RenderBarrierAccessFlags_t.ShaderReadBit );
	}

	/// <summary>
	/// Make a colour target that was just drawn readable by compute on the async queue, as <see cref="BarrierToComputeRead"/>
	/// does depth.
	/// </summary>
	public void BarrierColorToComputeRead( GpuTexture texture )
	{
		context.TextureBarrierTransition( texture.Native(), -1, RenderBarrierPipelineStageFlags_t.ColorAttachmentOutputBit,
			RenderBarrierPipelineStageFlags_t.ComputeShaderBit, RenderImageLayout_t.RENDER_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL,
			RenderBarrierAccessFlags_t.ColorAttachmentWriteBit, RenderBarrierAccessFlags_t.ShaderReadBit );
	}

	/// <summary>
	/// Make a texture compute wrote readable by pixel shaders, as <c>ResourceState.PixelShaderResource</c> does - without
	/// <c>Graphics</c>, so a worker thread can record it.
	/// </summary>
	public void BarrierComputeToPixelShaderRead( GpuTexture texture )
	{
		context.TextureBarrierTransition( texture.Native(), -1, RenderBarrierPipelineStageFlags_t.ComputeShaderBit,
			RenderBarrierPipelineStageFlags_t.FragmentShaderBit, RenderImageLayout_t.RENDER_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL,
			RenderBarrierAccessFlags_t.ShaderWriteBit, RenderBarrierAccessFlags_t.ShaderReadBit );
	}

	/// <summary>
	/// Make a depth texture that was just rendered readable by pixel shaders.
	/// </summary>
	public void BarrierToPixelShaderRead( GpuTexture depth )
	{
		context.TextureBarrierTransition( depth.Native(), -1, RenderBarrierPipelineStageFlags_t.LateFragmentTestsBit,
			RenderBarrierPipelineStageFlags_t.FragmentShaderBit, RenderImageLayout_t.RENDER_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL,
			RenderBarrierAccessFlags_t.DepthStencilAttachmentWriteBit, RenderBarrierAccessFlags_t.ShaderReadBit );
	}

	/// <summary>
	/// Set and cache viewport/depth range for restoration after custom drawing.
	/// </summary>
	public void SetViewport( Rect rect, float minZ = 0, float maxZ = 1 )
	{
		Bound = Bound with { Viewport = rect, MinZ = minZ, MaxZ = maxZ };
		var viewport = new RenderViewport( (int)rect.Left, (int)rect.Top, (int)rect.Width, (int)rect.Height ) { MinZ = minZ, MaxZ = maxZ };
		context.SetViewport( viewport );
	}

	public void Clear( Color color, bool clearColor = true, bool clearDepth = true )
	{
		context.Clear( new Vector4( color.r, color.g, color.b, color.a ), clearColor, clearDepth, clearDepth );
	}

	/// <summary>
	/// Upload a constant buffer and bind it to the attribute the shader's <c>cbuffer</c> is named after.
	/// </summary>
	public unsafe void SetConstants<T>( StringToken name, in T value ) where T : unmanaged
	{
		ResetMaterialBind();
		fixed ( T* ptr = &value )
		{
			RenderTools.SetDynamicConstantBufferData( Attributes.Engine().Get(), name, context, (IntPtr)ptr, Unsafe.SizeOf<T>() );
		}
	}


	/// <summary>
	/// Allocate a zeroed constant buffer beyond per-frame pool limits. Free with <see cref="DestroyConstantBuffer"/>.
	/// </summary>
	public static IntPtr CreateConstantBuffer( int size ) => RenderTools.CreateConstantBuffer( size );

	/// <summary>
	/// Free a buffer from <see cref="CreateConstantBuffer"/>.
	/// </summary>
	public static void DestroyConstantBuffer( IntPtr buffer ) => RenderTools.DestroyConstantBuffer( buffer );

	/// <summary>
	/// Upload and bind an owned constant buffer on this context.
	/// </summary>
	public unsafe void SetConstantBuffer<T>( StringToken name, IntPtr buffer, in T value ) where T : unmanaged
	{
		// New attributes under the draws
		ResetMaterialBind();
		fixed ( T* ptr = &value )
		{
			RenderTools.SetConstantBufferData( context, Attributes.Engine().Get(), name, buffer, (IntPtr)ptr, Unsafe.SizeOf<T>() );
		}
	}

	/// <summary>
	/// Bind model buffers and normal format before <see cref="SetRenderState"/>.
	/// </summary>
	public bool BindGeometry( RenderMesh mesh, in RenderMesh.Draw draw )
	{
		return RenderTools.BindModelDrawCall( context, drawAttributes, mesh.Geometry.Native(), draw.Mesh, draw.DrawCall );
	}

	/// <summary>
	/// Bind shader/state after setting draw attributes. Missing modes return false; native permits Forward-to-VrForward fallback
	/// (<c>CMaterial2::GetMode</c>). Nonzero biases override rasterizer bias; optional flags strip the pixel shader or use vertex-cache IDs.
	/// </summary>
	public bool SetRenderState( GpuMaterial material, StringToken shaderMode, RenderMesh mesh, in RenderMesh.Draw draw, int depthBias = 0, float slopeScaledDepthBias = 0, bool stripPixelShader = false, bool vertexCache = false )
	{
		ResetMaterialBind();
		var mode = material.Native().GetMode( shaderMode );
		if ( mode.IsNull ) return false;

		return RenderTools.SetRenderStateForModelDrawCall( context, drawAttributes, mode, mesh.Geometry.Native(), draw.Mesh, draw.DrawCall, depthBias, slopeScaledDepthBias, stripPixelShader, vertexCache, (int)OverlayStencil );
	}

	/// <summary>
	/// Prepass overlay-stencil mode (<c>ISceneLayer::SetOverlayStencil</c>); reset for each recording and pass.
	/// </summary>
	public OverlayStencil OverlayStencil
	{
		get;
		set
		{
			if ( field == value ) return;
			field = value;
			ResetMaterialBind();
		}
	}

	/// <summary>
	/// Bind and draw instances in one native call (<c>RenderTools.DrawModelDrawCall</c>), reusing similar-material state.
	/// Vertex-cache draws use the separate binding path. <paramref name="skinned"/> draws pick bones by blend index, so they
	/// keep their own input layout depth-only too.
	/// </summary>
	public bool DrawModel( GpuMaterial material, StringToken shaderMode, RenderMesh mesh, in RenderMesh.Draw draw, int firstInstance, int instances, int depthBias = 0, float slopeScaledDepthBias = 0, bool stripPixelShader = false, bool skinned = false )
	{
		return RenderTools.DrawModelDrawCall( context, drawAttributes, material.Native(), shaderMode, mesh.Geometry.Native(), draw.Mesh, draw.DrawCall,
			depthBias, slopeScaledDepthBias, stripPixelShader, (int)OverlayStencil, skinned, InstanceIds.Native(), firstInstance, instances, materialBind );
	}

	/// <summary>
	/// <see cref="DrawModel"/> with only the instances the occlusion cull kept for <paramref name="phase"/>
	/// (<c>RenderTools.DrawModelDrawCallIndirect</c>).
	/// </summary>
	public bool DrawModelCulled( GpuMaterial material, StringToken shaderMode, RenderMesh mesh, in RenderMesh.Draw draw, OcclusionPhase phase, int firstInstance, int argsIndex, int depthBias = 0, float slopeScaledDepthBias = 0, bool stripPixelShader = false, bool skinned = false )
	{
		return RenderTools.DrawModelDrawCallIndirect( context, drawAttributes, material.Native(), shaderMode, mesh.Geometry.Native(), draw.Mesh, draw.DrawCall,
			depthBias, slopeScaledDepthBias, stripPixelShader, (int)OverlayStencil, skinned, Occlusion.CulledIds.Native(), Occlusion.IdOffset( phase ) + firstInstance,
			Occlusion.Args.Native(), (Occlusion.ArgOffset( phase ) + argsIndex) * OcclusionCuller.ArgsStride, materialBind );
	}

	/// <summary>
	/// Draw <paramref name="count"/> draws of a model's buffers as one multi-draw, with <paramref name="draw"/>'s buffers and
	/// material (<c>RenderTools.DrawModelDrawCallMultiIndirect</c>): an aggregate's fragments. Each draw's base instance
	/// indexes the instance ids from <paramref name="firstInstance"/>.
	/// </summary>
	public bool DrawModelFragments( GpuMaterial material, StringToken shaderMode, RenderMesh mesh, in RenderMesh.Draw draw, int firstInstance, GpuBuffer args, int firstArg, int count, int depthBias = 0, float slopeScaledDepthBias = 0, bool stripPixelShader = false )
	{
		return RenderTools.DrawModelDrawCallMultiIndirect( context, drawAttributes, material.Native(), shaderMode, mesh.Geometry.Native(), draw.Mesh, draw.DrawCall,
			depthBias, slopeScaledDepthBias, stripPixelShader, (int)OverlayStencil, InstanceIds.Native(), firstInstance,
			args.Native(), firstArg * OcclusionCuller.ArgsStride, count, default, 0, materialBind );
	}

	/// <summary>
	/// <see cref="DrawModelFragments"/> with the fragments the occlusion cull kept for <paramref name="phase"/>: compacted, only the
	/// survivors, counted on the GPU; otherwise every fragment, the hidden ones with no instances.
	/// </summary>
	public bool DrawModelFragmentsCulled( GpuMaterial material, StringToken shaderMode, RenderMesh mesh, in RenderMesh.Draw draw, OcclusionPhase phase, int firstInstance, int cullArg, int count, int depthBias = 0, float slopeScaledDepthBias = 0, bool stripPixelShader = false )
	{
		var args = Occlusion.Args;
		var compact = Occlusion.CompactFragments;
		return RenderTools.DrawModelDrawCallMultiIndirect( context, drawAttributes, material.Native(), shaderMode, mesh.Geometry.Native(), draw.Mesh, draw.DrawCall,
			depthBias, slopeScaledDepthBias, stripPixelShader, (int)OverlayStencil, InstanceIds.Native(), firstInstance,
			args.Native(), Occlusion.FragmentArgs( phase, cullArg, count ) * OcclusionCuller.ArgsStride, count,
			compact ? args.Native() : default, compact ? Occlusion.FragmentCountArg( phase, cullArg, count ) * OcclusionCuller.ArgsStride : 0, materialBind );
	}

	/// <summary>
	/// Whether a multi-draw can take its count from the GPU (<c>RenderTools.SupportsDrawIndirectCount</c>).
	/// </summary>
	public static bool SupportsDrawIndirectCount => supportsDrawIndirectCount ??= RenderTools.SupportsDrawIndirectCount();
	static bool? supportsDrawIndirectCount;



	/// <summary>
	/// Shared shader/feature key for partial material rebinds (<c>RenderTools.GetMaterialSimilarityKey</c>).
	/// </summary>
	public static ulong SimilarityKey( GpuMaterial material ) => material.SimilarityKey;

	/// <summary>
	/// Copy data into a GPU buffer, recorded on this context so it's ordered before later draws.
	/// </summary>
	public void UploadBuffer<T>( GpuBuffer buffer, ReadOnlySpan<T> data ) where T : unmanaged => UploadBuffer( buffer, data, 0 );

	/// <summary>
	/// Copy data into a GPU buffer from <paramref name="offsetBytes"/> in, recorded on this context so it's ordered before later draws.
	/// </summary>
	public unsafe void UploadBuffer<T>( GpuBuffer buffer, ReadOnlySpan<T> data, int offsetBytes ) where T : unmanaged
	{
		fixed ( T* ptr = data )
		{
			RenderTools.SetGPUBufferData( context, buffer.Native(), (IntPtr)ptr, (uint)(data.Length * Unsafe.SizeOf<T>()), (uint)offsetBytes );
		}
	}


	/// <summary>
	/// Bind instance IDs at <paramref name="firstInstance"/> (<c>CSceneSystem::BindTransformSlot</c>).
	/// </summary>
	public void BindInstances( int stream, int firstInstance )
	{
		context.BindVertexBuffer( stream, InstanceIds.Native(), firstInstance * sizeof( uint ), sizeof( uint ) );
	}

	/// <summary>
	/// The frame's instance id stream - <see cref="TransformBuffer.InstanceIds"/>.
	/// </summary>
	public GpuBuffer<uint> InstanceIds { get; set; }

	/// <summary>
	/// This frame's occlusion cull, or null when not culling.
	/// </summary>
	public OcclusionCuller Occlusion { get; set; }

	public void BufferBarrier( GpuBuffer buffer, RenderBarrierPipelineStageFlags_t srcStage, RenderBarrierAccessFlags_t srcAccess, RenderBarrierPipelineStageFlags_t dstStage, RenderBarrierAccessFlags_t dstAccess )
	{
		context.BufferBarrierTransition( buffer.Native(), srcStage, dstStage, srcAccess, dstAccess );
	}

	/// <summary>
	/// Resolve depth and build min/max mips, as native's pipeline does (<see cref="SharedPasses.BuildDepthChain"/>).
	/// </summary>
	public void BuildDepthChain( GpuTexture depth, bool multisampled, GpuTexture chain, int width, int height, DepthResolveMode resolve = DepthResolveMode.Sample0 )
	{
		Invalidate();
		SharedPasses.BuildDepthChain( context, depth.Engine(), chain.Engine(), multisampled, width, height, resolve, fuseResolve: !AsyncCompute );
		// Finish the producer's layout transition before graphics and compute sample the chain concurrently.
		if ( AsyncCompute )
		{
			context.TextureBarrierTransition( chain.Native(), -1, RenderBarrierPipelineStageFlags_t.ComputeShaderBit,
				RenderBarrierPipelineStageFlags_t.ComputeShaderBit, RenderImageLayout_t.RENDER_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL,
				RenderBarrierAccessFlags_t.ShaderWriteBit, RenderBarrierAccessFlags_t.ShaderReadBit );
		}
	}

	/// <summary>
	/// Zero an occlusion debug grid before the frame's first cull.
	/// </summary>
	public void ClearOcclusionDebug( Sandbox.Rendering.OcclusionDebugTarget debug )
	{
		Invalidate();
		debug.Clear( context );
	}

	/// <summary>
	/// Resolve an occlusion debug grid for the overlay after the late cull.
	/// </summary>
	public void ResolveOcclusionDebug( Sandbox.Rendering.OcclusionDebugTarget debug, GpuTexture chain, bool frozen )
	{
		Invalidate();
		debug.Resolve( context, chain.Engine(), native: false, frozen );
	}

	static readonly StringToken SourceDepth = new( "SourceDepth" );
	static readonly StringToken FrameBufferCopyTexture = new( "FrameBufferCopyTexture" );
	static readonly StringToken FrameBufferCopyRectangle = new( "FrameBufferCopyRectangle" );

	// Lazy frame copy for the active translucent layer.
	GpuTexture frameBufferCopy;
	Rect frameBufferCopyViewport;
	bool frameBufferCopied;

	/// <summary>
	/// Enable one lazy frame copy for this translucent layer (<c>sc_max_framebuffer_copies_per_layer</c>).
	/// </summary>
	public void BeginFrameBufferReads( GpuTexture copy, Rect viewport )
	{
		frameBufferCopy = copy;
		frameBufferCopyViewport = viewport;
		frameBufferCopied = false;
	}

	/// <summary>
	/// Disable frame copies after the translucent layer.
	/// </summary>
	public void EndFrameBufferReads() => frameBufferCopy = null;

	/// <summary>
	/// Copy, depth-aware blur and bind the viewport on its first reader (<c>CSceneSystem::MakeFrameBufferCopy</c>).
	/// A no-op outside an enabled translucent layer.
	/// </summary>
	public void CopyFrameBuffer()
	{
		if ( frameBufferCopy is null || frameBufferCopied ) return;
		frameBufferCopied = true;

		ResetMaterialBind();
		var viewport = frameBufferCopyViewport;
		RenderTools.MakeFrameBufferCopy( context, frameBufferCopy.Native(), (int)viewport.Left, (int)viewport.Top, (int)viewport.Width, (int)viewport.Height );

		Attributes.Set( FrameBufferCopyTexture, frameBufferCopy );
		Attributes.Set( FrameBufferCopyRectangle, new Vector4( 0, 0, viewport.Width / frameBufferCopy.Width, viewport.Height / frameBufferCopy.Height ) );
	}

	/// <summary>
	/// Quarter-resolution depth blit (<see cref="SharedPasses.DownsampleQuarterDepth"/>), with isolated combos and context view constants.
	/// </summary>
	public void DownsampleQuarterDepth( Graphics.ManagedView target, GpuTexture depth, bool multisampled )
	{
		Invalidate();

		var attributes = RenderAttributes.Pool.Get();
		attributes.Set( SourceDepth, depth );
		using ( new Graphics.Scope( context, target ) )
		{
			SharedPasses.DownsampleQuarterDepth( attributes, multisampled );
		}

		RenderAttributes.Pool.Return( attributes );
	}

	/// <summary>
	/// Render a contact-shadow mask using <c>ContactShadows.Render</c>. Direction points toward the sun.
	/// </summary>
	public void RenderContactShadows( Graphics.ManagedView frame, GpuTexture mask, Matrix worldToProjection, Vector3 lightDirection, float shadowHardness,
		Sandbox.Rendering.ContactShadows.Steps steps = Sandbox.Rendering.ContactShadows.Steps.All )
	{
		Invalidate();
		using ( new Graphics.Scope( context, frame ) )
		{
			Sandbox.Rendering.ContactShadows.Render( mask.Engine(), worldToProjection, lightDirection, shadowHardness, steps );
		}
	}

	/// <summary>
	/// Dispatch thread counts with current attributes; native converts to shader group counts.
	/// </summary>
	public void Dispatch( GpuMaterial computeShader, int x, int y, int z )
	{
		Invalidate();
		RenderTools.Compute( context, Attributes.Engine().Get(), computeShader.Native().GetMode(), x, y, z );
	}

	/// <summary>
	/// Draw a model draw call. Its buffers are bound at its own offsets, so there's no base vertex.
	/// </summary>
	public void DrawIndexedInstanced( in RenderMesh.Draw draw, int instanceCount )
	{
		context.DrawIndexedInstanced( (RenderPrimitiveType)draw.PrimitiveType, draw.StartIndex, draw.IndexCount, instanceCount, draw.VertexCount, 0 );
	}
}

/// <summary>
/// What a model draw does with the depth prepass's game overlay stencil bit (<see cref="RenderContext.OverlayStencil"/>).
/// </summary>
internal enum OverlayStencil
{
	/// <summary>Nothing.</summary>
	None,

	/// <summary>Claim overlay pixels at their real depth.</summary>
	Write,

	/// <summary>Exclude claimed overlay pixels from the world prepass.</summary>
	Test,
}
