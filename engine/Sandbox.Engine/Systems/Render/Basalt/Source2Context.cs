using Basalt.Culling;
using NativeEngine;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Basalt.Gpu.Source2;

/// <summary>
/// Basalt's recording context over a Source 2 render context: frame contexts set up, segment contexts record passes
/// (<c>CRenderBatchList</c>). Attributes form frame, segment and draw levels, keeping concurrent writes isolated.
/// </summary>
internal sealed class Source2Context : GpuContext
{
	IRenderContext context;
	CRenderAttributes nativeAttributes;
	CRenderAttributes drawAttributes;
	bool drawAttributesMerged;

	// Re-merge after engine drawing, which may mutate attributes in place.
	ShaderParameters mergedObject, mergedLightmap;
	bool mergedValid;
	bool borrowed;
	Source2Context frame;
	bool debugMarkers;

	internal override bool GpuScopesEnabled => RenderHost.Current.GpuTimings || debugMarkers;

	/// <summary>
	/// A frame's own context.
	/// </summary>
	public Source2Context()
	{
	}

	/// <summary>
	/// Create a segment context whose attributes override the frame's.
	/// </summary>
	public Source2Context( GpuContext frame )
	{
		ArgumentNullException.ThrowIfNull( frame );
		this.frame = (Source2Context)frame;
	}

	/// <summary>
	/// Record a frame segment into its own or a shared native context.
	/// </summary>
	public override void BeginSegment( GpuContext frame, GpuContext shared )
	{
		if ( this.frame is null || frame is null ) throw new InvalidOperationException( "Only a segment context records a frame's segments" );
		this.frame = (Source2Context)frame;
		Begin( shared );
	}

	/// <summary>
	/// Record on the async compute queue (<c>RCFLAG_ASYNC_COMPUTE_QUEUE</c>): compute only. Set before <see cref="Begin"/>.
	/// </summary>
	public override bool AsyncCompute { get; set; }

	const uint AsyncComputeQueue = 0x0008;


	/// <summary>
	/// Signal a semaphore when this context's work is done, for another to <see cref="WaitAtBegin"/>. Once per context,
	/// after recording.
	/// </summary>
	public override GpuSemaphore SignalAtEnd() => new( context.SemaphoreSignalAtEnd() );

	/// <summary>
	/// Start this context's work only once another's <see cref="SignalAtEnd"/> has signalled. Once per context, before it's
	/// submitted.
	/// </summary>
	public override void WaitAtBegin( GpuSemaphore semaphore ) => context.SemaphoreWaitAtBegin( semaphore.Handle );

	/// <summary>
	/// Whether this is the frame's setup context.
	/// </summary>
	internal override bool IsFrame => frame is null;

	/// <summary>
	/// View constants and pass attributes, created on first <see cref="Begin"/> to allow engine-free construction.
	/// </summary>
	internal override ShaderParameters Attributes => attributes;
	ShaderParameters attributes;

	/// <summary>
	/// Cached native material bind for similar-material rebinds (<c>RenderTools.DrawModelDrawCall</c>).
	/// Invalidate whenever draw inputs change.
	/// </summary>
	IntPtr materialBind;

	/// <summary>
	/// Force a full material bind after external attribute changes or drawing.
	/// </summary>
	public override void ResetMaterialBind()
	{
		if ( materialBind != IntPtr.Zero ) RenderTools.ResetMaterialBind( materialBind );
	}

	/// <summary>
	/// Full and similar-material binds this context's draws have made, ever (<c>RenderTools.GetMaterialBindCounts</c>). A
	/// range of draws made the difference.
	/// </summary>
	internal override unsafe (int Full, int Similar) MaterialBinds
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

	public override unsafe void Dispose()
	{
		fullscreenView?.Dispose();
		fullscreenView = null;

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
		attributes = null;
	}

	internal override bool IsRecording => !context.IsNull;

	/// <summary>
	/// Native context for engine drawing APIs.
	/// </summary>
	internal IRenderContext Native => context;


	/// <summary>
	/// Last targets, viewport and depth range; restored after custom drawing.
	/// </summary>
	internal override StageTarget Bound => bound;
	StageTarget bound;

	/// <summary>
	/// Whether cached target bindings remain valid.
	/// </summary>
	bool boundValid;

	/// <summary>
	/// Bind targets, viewport and depth range. Reuse unchanged bindings to keep adjacent passes in one native render pass.
	/// </summary>
	public override void Bind( in StageTarget target )
	{
		if ( boundValid && SameTargets( Bound, target ) )
		{
			if ( Bound.Viewport != target.Viewport || Bound.MinZ != target.MinZ || Bound.MaxZ != target.MaxZ ) SetViewport( target.Viewport, target.MinZ, target.MaxZ );
			bound = target;
			return;
		}

		ResetMaterialBind();
		if ( target.Color is null ) RenderTools.BindDepthTarget( context, target.Depth.Native(), target.DepthSlice );
		else RenderTools.BindColorAndDepthTarget( context, target.Color.Native(), target.Depth.Native(), target.SrgbWrite );
		boundValid = true;

		bound = target;
		SetViewport( target.Viewport, target.MinZ, target.MaxZ );
	}

	static bool SameTargets( in StageTarget a, in StageTarget b )
	{
		return a.Color == b.Color && a.Depth == b.Depth && a.DepthSlice == b.DepthSlice && a.SrgbWrite == b.SrgbWrite;
	}

	/// <summary>
	/// Restore cached targets and viewport after engine drawing.
	/// </summary>
	internal override void RestoreBound()
	{
		Invalidate();
		Bind( Bound );
	}

	/// <summary>
	/// Invalidate target, attribute and material caches after external drawing.
	/// </summary>
	internal override void Invalidate()
	{
		boundValid = false;
		mergedValid = false;
		ResetMaterialBind();
	}

	static readonly StringToken ViewConstantsName = new( "PerViewConstantBuffer_t" );

	/// <summary>
	/// Current view-constant key; null for unknown or unkeyed shadow constants.
	/// </summary>
	internal override ViewConstantsKey? ViewConstants => viewConstants;
	ViewConstantsKey? viewConstants;

	/// <summary>
	/// Bind <c>PerViewConstantBuffer_t</c> and cache its key.
	/// </summary>
	public override void SetViewConstants( in ViewConstants constants, ViewConstantsKey? key )
	{
		SetConstants( ViewConstantsName, constants );
		viewConstants = key;
	}

	/// <summary>
	/// Cache the frame constants inherited by this segment.
	/// </summary>
	internal override void InheritViewConstants( ViewConstantsKey key ) => viewConstants = key;

	/// <summary>
	/// Set a combo and invalidate the material bind.
	/// </summary>
	public override void SetCombo( StringToken name, int value )
	{
		Attributes.SetCombo( name, value );
		ResetMaterialBind();
	}

	/// <summary>
	/// Set a texture and invalidate the material bind.
	/// </summary>
	public override void Set( StringToken name, GpuTexture value )
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
	public override void SetPassCombo( StringToken name, int value )
	{
		RememberPassState( name, combo: true );
		SetCombo( name, value );
	}

	/// <summary>
	/// Set a pass-local attribute, reset to zero by <see cref="EndPass"/>.
	/// </summary>
	public override void SetPassAttribute( StringToken name, int value )
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
	public override void EndPass()
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
	public override GpuScope BeginGpuScope( string name )
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


	static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Sandbox.Rendering.ProfilingSampler> gpuScopeNames = new();

	/// <summary>
	/// End a scope <see cref="BeginGpuScope"/> started.
	/// </summary>
	public override void EndGpuScope( GpuScope scope )
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
	static GpuTexture SwapChainColor( GpuSwapChain swapChain ) => Source2RawTexture.From( g_pRenderDevice.GetSwapChainTexture( swapChain.Handle, SwapChainBuffer.BufferColor ) );

	/// <summary>
	/// Merge lightmap then object attributes above context attributes (<c>CBaseSceneObjectDesc::DrawArray</c>).
	/// </summary>
	public override void SetObjectAttributes( ShaderParameters objectAttributes, ShaderParameters lightmap = null )
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
	/// Compute-skin a model mesh's instances into native vertex-cache blocks (<c>skinning_cs</c>).
	/// Optional per-instance volume ranges enable deformation; dispatch deformed and undeformed instances separately. Deformed
	/// instances read their anchors, per mesh bone, from their offset into <paramref name="anchors"/> - 0 for none.
	/// </summary>
	public override unsafe bool DispatchSkinning( GpuMaterial skinningShader, RenderMesh mesh, int modelMesh, GpuBuffer transforms, ReadOnlySpan<int> slots, ReadOnlySpan<int> vertexCacheOffsets, int blendWeights,
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
	public override void GenerateMorphs()
	{
		RenderTools.GenerateMorphs( context );
		boundValid = false;
		ResetMaterialBind();
	}

	/// <summary>
	/// Before skinning: vertex shaders from earlier work that read the cache finish before compute writes it.
	/// </summary>
	public override void BarrierVertexCacheToWrite()
	{
		context.BufferBarrierTransition( RenderTools.GetVertexCacheBuffer(), RenderBarrierPipelineStageFlags_t.PreRasterizationShadersBit, RenderBarrierPipelineStageFlags_t.ComputeShaderBit,
			RenderBarrierAccessFlags_t.ShaderReadBit, RenderBarrierAccessFlags_t.ShaderWriteBit );
	}

	/// <summary>
	/// After skinning: the writes are visible to the vertex shaders that read the cache.
	/// </summary>
	public override void BarrierVertexCacheToRead()
	{
		context.BufferBarrierTransition( RenderTools.GetVertexCacheBuffer(), RenderBarrierPipelineStageFlags_t.ComputeShaderBit, RenderBarrierPipelineStageFlags_t.PreRasterizationShadersBit,
			RenderBarrierAccessFlags_t.ShaderWriteBit, RenderBarrierAccessFlags_t.ShaderReadBit );
	}

	/// <summary>
	/// Bind vertex-cache ID streams starting at stream 2, after instance IDs.
	/// </summary>
	public override bool BindVertexCacheIds( RenderMesh mesh, in RenderMesh.Draw draw )
	{
		return RenderTools.BindModelDrawCallVertexIds( context, mesh.Geometry.Native(), draw.Mesh, draw.DrawCall, 2 );
	}

	/// <summary>
	/// Draw a <c>SceneSkyBox</c> into the bound target using view attributes.
	/// </summary>
	public override bool DrawSky( SceneLighting lighting, StringToken shaderMode )
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
	/// Begin recording into an owned or shared native context. Only the owner submits a shared context.
	/// </summary>
	public override void Begin( GpuContext shared = null )
	{
		// Frame setup is main-thread-only; segments begin on their recording thread.
		if ( frame is null ) ThreadSafe.AssertIsMainThread();
		if ( IsRecording ) throw new InvalidOperationException( "Already recording" );

		if ( Attributes is null )
		{
			// Dispose explicitly before device shutdown; finalization may be too late.
			nativeAttributes = CRenderAttributes.Create();
			attributes = Source2Parameters.From( new RenderAttributes( nativeAttributes ) );
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
		bound = default;
		viewConstants = null;
		passStateCount = 0;

		// Material binds cannot survive across frames. Zeroed, so its bind counts start at nothing.
		if ( materialBind == IntPtr.Zero ) materialBind = AllocMaterialBind();
		ResetMaterialBind();

		borrowed = shared is not null;
		if ( borrowed && AsyncCompute ) throw new InvalidOperationException( "An async compute context records into its own native context" );
		context = borrowed ? ((Source2Context)shared).context : g_pRenderDevice.CreateRenderContext( AsyncCompute ? AsyncComputeQueue : 0 );
		debugMarkers = borrowed ? ((Source2Context)shared).debugMarkers : g_pRenderDevice.AreDebugMarkersEnabled();

		// The frame's bindless texture set, as CSceneSystem::InitializeRenderAttributes does for a view
		if ( frame is null ) Attributes.Engine().Get().SetGlobalBindlessDescriptorSet();

		// View constants resolve through context attributes, not draw parents (CRenderBatchList::Start).
		context.GetAttributesPtrForModify().SetParent( nativeAttributes );
	}

	/// <summary>
	/// Submit and end recording. Name the swap chain only on the first submission to include all work in GPU timing.
	/// </summary>
	public override void Submit( GpuSwapChain swapChain = default )
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
	public override void SubmitNext()
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
	/// Stop recording without submitting - what was recorded is dropped.
	/// </summary>
	public override void End()
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
	public override void CopyToSwapChain( in RenderOutput output, Rect viewport )
	{
		if ( !output.HasCopy ) return;

		Invalidate();
		if ( output.Texture is { } texture ) RenderTools.BindColorAndDepthTarget( context, (output.Scratch ?? texture).Native(), default, !texture.Format.IsFloat() );
		else context.BindRenderTargets( output.SwapChain.Handle, true, false );

		RenderTools.BlitTexture( context, output.Target.Resolved.Native(), (int)viewport.Left, (int)viewport.Top, (int)viewport.Width, (int)viewport.Height, 0 );
	}

	/// <summary>
	/// Final colour target and its sRGB-write requirement.
	/// </summary>
	public override GpuTexture OutputColor( in RenderOutput output, out bool srgbWrite )
	{
		if ( output.Texture is { } texture )
		{
			srgbWrite = !texture.Format.IsFloat();
			return output.Scratch ?? texture;
		}

		if ( output.SwapChain.IsValid )
		{
			srgbWrite = true;
			return SwapChainColor( output.SwapChain );
		}

		srgbWrite = output.Target.SrgbWrite;
		return output.Target.Samples > 1 ? output.Target.Resolved : output.Target.Color;
	}

	/// <summary>
	/// Resolve bound MSAA colour into <see cref="ViewTarget.Resolved"/> (<c>CSceneSystem::ResolveLayer</c>).
	/// </summary>
	public override void Resolve( in RenderOutput output )
	{
		if ( output.Target is not { Samples: > 1 } target ) return;

		var rect = new NativeRect( 0, 0, target.Size.x, target.Size.y );
		RenderTools.ResolveFrameBuffer( context, target.Resolved.Native(), rect );
	}

	/// <summary>
	/// Resolve a multisampled texture output's <see cref="RenderOutput.Scratch"/> into the texture, once everything has drawn.
	/// </summary>
	public override void ResolveScratch( in RenderOutput output )
	{
		if ( output.Scratch is not { } scratch || output.Texture is not { } texture ) return;

		Invalidate();
		RenderTools.BindColorAndDepthTarget( context, scratch.Native(), default, !texture.Format.IsFloat() );
		RenderTools.ResolveFrameBuffer( context, texture.Native(), new NativeRect( 0, 0, texture.Width, texture.Height ) );
	}

	/// <summary>
	/// Copy the first array slices or cube faces between equal-sized textures.
	/// </summary>
	public override void CopyTexture( GpuTexture source, GpuTexture destination, int slices )
	{
		var rect = new NativeRect( 0, 0, source.Width, source.Height );
		for ( uint slice = 0; slice < slices; slice++ )
			RenderTools.CopyTexture( context, source.Native(), destination.Native(), rect, 0, 0, 0, slice, 0, slice );
	}

	/// <summary>
	/// Transition depth to writable after prior pixel-shader reads.
	/// </summary>
	public override void BarrierToDepthWrite( GpuTexture depth )
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
	public override void BarrierToComputeRead( GpuTexture depth )
	{
		context.TextureBarrierTransition( depth.Native(), -1, RenderBarrierPipelineStageFlags_t.LateFragmentTestsBit,
			RenderBarrierPipelineStageFlags_t.ComputeShaderBit, RenderImageLayout_t.RENDER_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL,
			RenderBarrierAccessFlags_t.DepthStencilAttachmentWriteBit, RenderBarrierAccessFlags_t.ShaderReadBit );
	}

	/// <summary>
	/// Make a colour target that was just drawn readable by compute on the async queue, as <see cref="BarrierToComputeRead"/>
	/// does depth.
	/// </summary>
	public override void BarrierColorToComputeRead( GpuTexture texture )
	{
		context.TextureBarrierTransition( texture.Native(), -1, RenderBarrierPipelineStageFlags_t.ColorAttachmentOutputBit,
			RenderBarrierPipelineStageFlags_t.ComputeShaderBit, RenderImageLayout_t.RENDER_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL,
			RenderBarrierAccessFlags_t.ColorAttachmentWriteBit, RenderBarrierAccessFlags_t.ShaderReadBit );
	}

	/// <summary>
	/// Make a texture compute wrote readable by pixel shaders, as <c>ResourceState.PixelShaderResource</c> does - without
	/// <c>Graphics</c>, so a worker thread can record it.
	/// </summary>
	public override void BarrierComputeToPixelShaderRead( GpuTexture texture )
	{
		context.TextureBarrierTransition( texture.Native(), -1, RenderBarrierPipelineStageFlags_t.ComputeShaderBit,
			RenderBarrierPipelineStageFlags_t.FragmentShaderBit, RenderImageLayout_t.RENDER_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL,
			RenderBarrierAccessFlags_t.ShaderWriteBit, RenderBarrierAccessFlags_t.ShaderReadBit );
	}

	/// <summary>
	/// Make a depth texture that was just rendered readable by pixel shaders.
	/// </summary>
	public override void BarrierToPixelShaderRead( GpuTexture depth )
	{
		context.TextureBarrierTransition( depth.Native(), -1, RenderBarrierPipelineStageFlags_t.LateFragmentTestsBit,
			RenderBarrierPipelineStageFlags_t.FragmentShaderBit, RenderImageLayout_t.RENDER_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL,
			RenderBarrierAccessFlags_t.DepthStencilAttachmentWriteBit, RenderBarrierAccessFlags_t.ShaderReadBit );
	}

	/// <summary>
	/// Set and cache viewport/depth range for restoration after custom drawing.
	/// </summary>
	public override void SetViewport( Rect rect, float minZ = 0, float maxZ = 1 )
	{
		bound = bound with { Viewport = rect, MinZ = minZ, MaxZ = maxZ };
		var viewport = new RenderViewport( (int)rect.Left, (int)rect.Top, (int)rect.Width, (int)rect.Height ) { MinZ = minZ, MaxZ = maxZ };
		context.SetViewport( viewport );
	}

	public override void Clear( Color color, bool clearColor = true, bool clearDepth = true )
	{
		context.Clear( new Vector4( color.r, color.g, color.b, color.a ), clearColor, clearDepth, clearDepth );
	}

	/// <summary>
	/// Upload a constant buffer and bind it to the attribute the shader's <c>cbuffer</c> is named after.
	/// </summary>
	public override unsafe void SetConstants<T>( StringToken name, in T value )
	{
		ResetMaterialBind();
		fixed ( T* ptr = &value )
		{
			RenderTools.SetDynamicConstantBufferData( Attributes.Engine().Get(), name, context, (IntPtr)ptr, Unsafe.SizeOf<T>() );
		}
	}




	/// <summary>
	/// Upload and bind an owned constant buffer on this context.
	/// </summary>
	public override unsafe void SetConstantBuffer<T>( StringToken name, IntPtr buffer, in T value )
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
	public override bool BindGeometry( RenderMesh mesh, in RenderMesh.Draw draw )
	{
		return RenderTools.BindModelDrawCall( context, drawAttributes, mesh.Geometry.Native(), draw.Mesh, draw.DrawCall );
	}

	/// <summary>
	/// Bind shader/state after setting draw attributes. Missing modes return false; native permits Forward-to-VrForward fallback
	/// (<c>CMaterial2::GetMode</c>). Nonzero biases override rasterizer bias; optional flags strip the pixel shader or use vertex-cache IDs.
	/// </summary>
	public override bool SetRenderState( GpuMaterial material, StringToken shaderMode, RenderMesh mesh, in RenderMesh.Draw draw, int depthBias = 0, float slopeScaledDepthBias = 0, bool stripPixelShader = false, bool vertexCache = false )
	{
		ResetMaterialBind();
		var mode = material.Native().GetMode( shaderMode );
		if ( mode.IsNull ) return false;

		return RenderTools.SetRenderStateForModelDrawCall( context, drawAttributes, mode, mesh.Geometry.Native(), draw.Mesh, draw.DrawCall, depthBias, slopeScaledDepthBias, stripPixelShader, vertexCache, (int)OverlayStencil );
	}

	/// <summary>
	/// Prepass overlay-stencil mode (<c>ISceneLayer::SetOverlayStencil</c>); reset for each recording and pass.
	/// </summary>
	public override OverlayStencil OverlayStencil
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
	public override bool DrawModel( GpuMaterial material, StringToken shaderMode, RenderMesh mesh, in RenderMesh.Draw draw, int firstInstance, int instances, int depthBias = 0, float slopeScaledDepthBias = 0, bool stripPixelShader = false, bool skinned = false )
	{
		return RenderTools.DrawModelDrawCall( context, drawAttributes, material.Native(), shaderMode, mesh.Geometry.Native(), draw.Mesh, draw.DrawCall,
			depthBias, slopeScaledDepthBias, stripPixelShader, (int)OverlayStencil, skinned, InstanceIds.Native(), firstInstance, instances, materialBind );
	}

	/// <summary>
	/// <see cref="DrawModel"/> with only the instances the occlusion cull kept for <paramref name="phase"/>
	/// (<c>RenderTools.DrawModelDrawCallIndirect</c>).
	/// </summary>
	public override bool DrawModelCulled( GpuMaterial material, StringToken shaderMode, RenderMesh mesh, in RenderMesh.Draw draw, OcclusionPhase phase, int firstInstance, int argsIndex, int depthBias = 0, float slopeScaledDepthBias = 0, bool stripPixelShader = false, bool skinned = false )
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
	public override bool DrawModelFragments( GpuMaterial material, StringToken shaderMode, RenderMesh mesh, in RenderMesh.Draw draw, int firstInstance, GpuBuffer args, int firstArg, int count, int depthBias = 0, float slopeScaledDepthBias = 0, bool stripPixelShader = false )
	{
		return RenderTools.DrawModelDrawCallMultiIndirect( context, drawAttributes, material.Native(), shaderMode, mesh.Geometry.Native(), draw.Mesh, draw.DrawCall,
			depthBias, slopeScaledDepthBias, stripPixelShader, (int)OverlayStencil, InstanceIds.Native(), firstInstance,
			args.Native(), firstArg * OcclusionCuller.ArgsStride, count, default, 0, materialBind );
	}

	/// <summary>
	/// <see cref="DrawModelFragments"/> with the fragments the occlusion cull kept for <paramref name="phase"/>: compacted, only the
	/// survivors, counted on the GPU; otherwise every fragment, the hidden ones with no instances.
	/// </summary>
	public override bool DrawModelFragmentsCulled( GpuMaterial material, StringToken shaderMode, RenderMesh mesh, in RenderMesh.Draw draw, OcclusionPhase phase, int firstInstance, int cullArg, int count, int depthBias = 0, float slopeScaledDepthBias = 0, bool stripPixelShader = false )
	{
		var args = Occlusion.Args;
		var compact = Occlusion.CompactFragments;
		return RenderTools.DrawModelDrawCallMultiIndirect( context, drawAttributes, material.Native(), shaderMode, mesh.Geometry.Native(), draw.Mesh, draw.DrawCall,
			depthBias, slopeScaledDepthBias, stripPixelShader, (int)OverlayStencil, InstanceIds.Native(), firstInstance,
			args.Native(), Occlusion.FragmentArgs( phase, cullArg, count ) * OcclusionCuller.ArgsStride, count,
			compact ? args.Native() : default, compact ? Occlusion.FragmentCountArg( phase, cullArg, count ) * OcclusionCuller.ArgsStride : 0, materialBind );
	}





	/// <summary>
	/// Copy data into a GPU buffer, recorded on this context so it's ordered before later draws.
	/// </summary>

	/// <summary>
	/// Copy data into a GPU buffer from <paramref name="offsetBytes"/> in, recorded on this context so it's ordered before later draws.
	/// </summary>
	public override unsafe void UploadBuffer<T>( GpuBuffer buffer, ReadOnlySpan<T> data, int offsetBytes )
	{
		fixed ( T* ptr = data )
		{
			RenderTools.SetGPUBufferData( context, buffer.Native(), (IntPtr)ptr, (uint)(data.Length * Unsafe.SizeOf<T>()), (uint)offsetBytes );
		}
	}


	/// <summary>
	/// Bind instance IDs at <paramref name="firstInstance"/> (<c>CSceneSystem::BindTransformSlot</c>).
	/// </summary>
	public override void BindInstances( int stream, int firstInstance )
	{
		context.BindVertexBuffer( stream, InstanceIds.Native(), firstInstance * sizeof( uint ), sizeof( uint ) );
	}

	/// <summary>
	/// The frame's instance id stream - <see cref="TransformBuffer.InstanceIds"/>.
	/// </summary>
	public override GpuBuffer<uint> InstanceIds { get; set; }

	/// <summary>
	/// This frame's occlusion cull, or null when not culling.
	/// </summary>
	public override OcclusionCuller Occlusion { get; set; }

	public override void BufferBarrier( GpuBuffer buffer, GpuStage srcStage, GpuAccess srcAccess, GpuStage dstStage, GpuAccess dstAccess )
	{
		context.BufferBarrierTransition( buffer.Native(), (RenderBarrierPipelineStageFlags_t)srcStage, (RenderBarrierPipelineStageFlags_t)dstStage, (RenderBarrierAccessFlags_t)srcAccess, (RenderBarrierAccessFlags_t)dstAccess );
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
	public override void BeginFrameBufferReads( GpuTexture copy, Rect viewport )
	{
		frameBufferCopy = copy;
		frameBufferCopyViewport = viewport;
		frameBufferCopied = false;
	}

	/// <summary>
	/// Disable frame copies after the translucent layer.
	/// </summary>
	public override void EndFrameBufferReads() => frameBufferCopy = null;

	static readonly ConditionalWeakTable<GpuReadback, AsyncGPUReadback.BufferReadDelegate> readbacks = new();

	public override void ReadBuffer( GpuBuffer buffer, int offset, int bytes, GpuReadback callback )
		=> context.ReadBufferAsync( buffer.Engine(), readbacks.GetValue( callback, static c => new AsyncGPUReadback.BufferReadDelegate( c ) ), offset, bytes );

	/// <summary>
	/// Copy, depth-aware blur and bind the viewport on its first reader (<c>CSceneSystem::MakeFrameBufferCopy</c>).
	/// A no-op outside an enabled translucent layer.
	/// </summary>
	public override void CopyFrameBuffer()
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
	/// Dispatch thread counts with current attributes; native converts to shader group counts.
	/// </summary>
	public override void Dispatch( GpuMaterial computeShader, int x, int y, int z )
	{
		Invalidate();
		RenderTools.Compute( context, Attributes.Engine().Get(), computeShader.Native().GetMode(), x, y, z );
	}

	/// <summary>
	/// Dispatch thread counts with a pass's own parameters.
	/// </summary>
	public override void Dispatch( GpuMaterial computeShader, ShaderParameters parameters, int x, int y, int z )
	{
		RenderTools.Compute( context, parameters.Engine().Get(), computeShader.Native().GetMode(), x, y, z );
	}

	Graphics.ManagedView fullscreenView;

	/// <summary>
	/// Blit over the bound target, as the engine's <c>Graphics.Blit</c> does inside a view.
	/// </summary>
	public override void DrawFullscreen( GpuMaterial material, ShaderParameters parameters, StringToken shaderMode )
	{
		var target = Bound;
		fullscreenView ??= new Graphics.ManagedView();
		fullscreenView.Color = target.Color.Native();
		fullscreenView.Depth = target.Depth.Native();
		fullscreenView.SrgbWrite = target.SrgbWrite;
		fullscreenView.Viewport = target.Viewport;
		fullscreenView.MinZ = target.MinZ;
		fullscreenView.MaxZ = target.MaxZ;
		fullscreenView.ColorFormat = Source2Formats.ToEngineOrDefault( target.ColorFormat );
		fullscreenView.Msaa = Source2Formats.ToAmount( target.Samples );
		fullscreenView.LayerType = SceneLayerType.Opaque;
		fullscreenView.ShaderMode = shaderMode;

		using ( new Graphics.Scope( context, fullscreenView ) )
		{
			Graphics.Blit( material.Engine(), parameters.Engine() );
		}
	}

	public override void TextureBarrier( GpuTexture texture, GpuStage srcStage, GpuAccess srcAccess, GpuStage dstStage, GpuAccess dstAccess, TextureLayout layout )
	{
		var native = layout switch
		{
			TextureLayout.ShaderRead => RenderImageLayout_t.RENDER_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL,
			TextureLayout.ColorAttachment => RenderImageLayout_t.RENDER_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL,
			TextureLayout.DepthAttachment => RenderImageLayout_t.RENDER_IMAGE_LAYOUT_DEPTH_STENCIL_ATTACHMENT_OPTIMAL,
			_ => RenderImageLayout_t.RENDER_IMAGE_LAYOUT_GENERAL,
		};
		context.TextureBarrierTransition( texture.Native(), -1, (RenderBarrierPipelineStageFlags_t)srcStage, (RenderBarrierPipelineStageFlags_t)dstStage, native,
			(RenderBarrierAccessFlags_t)srcAccess, (RenderBarrierAccessFlags_t)dstAccess );
	}

	/// <summary>
	/// Draw a model draw call. Its buffers are bound at its own offsets, so there's no base vertex.
	/// </summary>
	public override void DrawIndexedInstanced( in RenderMesh.Draw draw, int instanceCount )
	{
		context.DrawIndexedInstanced( (RenderPrimitiveType)draw.PrimitiveType, draw.StartIndex, draw.IndexCount, instanceCount, draw.VertexCount, 0 );
	}
}

