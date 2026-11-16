namespace Borealis.Gpu;

/// <summary>
/// Records GPU work for a frame: frame contexts set up, segment contexts record passes (<c>CRenderBatchList</c>). The
/// <see cref="GpuDevice"/> makes them. Shader parameters form frame, segment and draw levels, so concurrent recording stays
/// isolated.
/// </summary>
internal abstract class GpuContext : IDisposable
{
	/// <summary>
	/// This thread's segment context, for engine drawing that records into it.
	/// </summary>
	[ThreadStatic] internal static GpuContext Recording;

	static GpuDevice Device => RenderHost.Current.Device;

	/// <summary>Whether the device has an async compute queue.</summary>
	public static bool SupportsAsyncCompute => Device.SupportsAsyncCompute;

	/// <summary>Whether the device can take an indirect draw count from a buffer.</summary>
	public static bool SupportsDrawIndirectCount => Device.SupportsDrawIndirectCount;

	/// <summary>Allocate vertex-cache space this frame and return its first vertex.</summary>
	public static int AllocateVertexCache( int vertices ) => Device.AllocateVertexCache( vertices );

	/// <summary>Where a queued morph is in the atlas, once generated.</summary>
	public static Vector4 MorphSubrect( int handle ) => Device.MorphSubrect( handle );

	/// <summary>Start a frame's submission, naming the swap chain it ends up in.</summary>
	public static void BeginSubmitting( GpuSwapChain swapChain ) => Device.BeginSubmitting( swapChain );

	/// <summary>The time shaders see this frame.</summary>
	public static float RenderTime => Device.RenderTime;

	/// <summary>A zeroed constant buffer beyond per-frame limits. Free with <see cref="DestroyConstantBuffer"/>.</summary>
	public static IntPtr CreateConstantBuffer( int size ) => Device.CreateConstantBuffer( size );

	/// <summary>Free a buffer from <see cref="CreateConstantBuffer"/>.</summary>
	public static void DestroyConstantBuffer( IntPtr buffer ) => Device.DestroyConstantBuffer( buffer );

	/// <summary>Whether a texture exists on the GPU.</summary>
	public static bool HasData( GpuTexture texture ) => texture is { IsValid: true };

	/// <summary>A texture's bindless sRGB index through its own dimension.</summary>
	public static uint BindlessIndex( GpuTexture texture ) => (uint)texture.ViewIndex( true, TextureViewDimension.Default );

	/// <summary>A material's similarity key.</summary>
	public static ulong SimilarityKey( GpuMaterial material ) => material.SimilarityKey;

	/// <summary>A GPU timing or debug marker scope, from <see cref="BeginGpuScope"/>.</summary>
	public readonly record struct GpuScope( IntPtr Marker, bool Pix );

	/// <summary>Record on the async compute queue: compute only. Set before <see cref="Begin"/>.</summary>
	public abstract bool AsyncCompute { get; set; }

	internal abstract bool GpuScopesEnabled { get; }

	/// <summary>Whether this is the frame's setup context.</summary>
	internal abstract bool IsFrame { get; }

	internal abstract bool IsRecording { get; }

	/// <summary>This context's frame or pass parameters.</summary>
	internal abstract ShaderParameters Attributes { get; }

	/// <summary>Full and similar-material binds this context's draws have made, ever.</summary>
	internal abstract (int Full, int Similar) MaterialBinds { get; }

	/// <summary>Last targets, viewport and depth range; restored after custom drawing.</summary>
	internal abstract StageTarget Bound { get; }

	/// <summary>Current view-constant key; null for unknown or unkeyed shadow constants.</summary>
	internal abstract ViewConstantsKey? ViewConstants { get; }

	/// <summary>Prepass overlay-stencil mode; reset for each recording and pass.</summary>
	public abstract OverlayStencil OverlayStencil { get; set; }

	/// <summary>The frame's instance ids, which draws bind at their first instance.</summary>
	public abstract GpuBuffer<uint> InstanceIds { get; set; }

	/// <summary>The frame's occlusion culler, whose culled ids and arguments culled draws read.</summary>
	public abstract OcclusionCuller Occlusion { get; set; }

	/// <summary>Record a frame segment into its own or a shared context.</summary>
	public abstract void BeginSegment( GpuContext frame, GpuContext shared );

	/// <summary>Begin recording into an owned or shared context. Only the owner submits a shared context.</summary>
	public abstract void Begin( GpuContext shared = null );

	/// <summary>
	/// After <see cref="BeginSegment"/> into a context of its own: the segment carries on the draws of the segment submitted
	/// before it (<paramref name="fromPrevious"/>), or the next one carries on its own (<paramref name="intoNext"/>), on the
	/// same targets, so their rendering can be one scope on the GPU. A device that places its own render passes ignores it.
	/// </summary>
	internal virtual void CarryRendering( bool fromPrevious, bool intoNext ) { }

	/// <summary>Submit and end recording, naming the swap chain on a frame's first submission.</summary>
	public abstract void Submit( GpuSwapChain swapChain = default );

	/// <summary>Submit after earlier work and end recording.</summary>
	public abstract void SubmitNext();

	/// <summary>Stop recording without submitting.</summary>
	public abstract void End();

	/// <summary>Signal a semaphore when this context's work is done. Once per context, after recording.</summary>
	public abstract GpuSemaphore SignalAtEnd();

	/// <summary>Start this context's work only once another's semaphore has signalled.</summary>
	public abstract void WaitAtBegin( GpuSemaphore semaphore );

	/// <summary>Release the context's own resources.</summary>
	public abstract void Dispose();

	/// <summary>Force a full material bind after external parameter changes or drawing.</summary>
	public abstract void ResetMaterialBind();

	/// <summary>Bind targets, viewport and depth range, reusing unchanged bindings.</summary>
	public abstract void Bind( in StageTarget target );

	/// <summary>Restore cached targets and viewport after engine drawing.</summary>
	internal abstract void RestoreBound();

	/// <summary>Invalidate target, parameter and material caches after external drawing.</summary>
	internal abstract void Invalidate();

	/// <summary>Bind the view constants and cache their key.</summary>
	public abstract void SetViewConstants( in ViewConstants constants, ViewConstantsKey? key );

	/// <summary>Cache the frame constants inherited by this segment.</summary>
	internal abstract void InheritViewConstants( ViewConstantsKey key );

	/// <summary>Set a combo.</summary>
	public abstract void SetCombo( StringToken name, int value );

	/// <summary>Set a texture.</summary>
	public abstract void Set( StringToken name, GpuTexture value );

	/// <summary>Set a pass-local combo, reset to zero by <see cref="EndPass"/>.</summary>
	public abstract void SetPassCombo( StringToken name, int value );

	/// <summary>Set a pass-local value, reset to zero by <see cref="EndPass"/>.</summary>
	public abstract void SetPassAttribute( StringToken name, int value );

	/// <summary>Reset pass-local values and overlay stencil.</summary>
	public abstract void EndPass();

	/// <summary>Start a named GPU scope.</summary>
	public abstract GpuScope BeginGpuScope( string name );

	/// <summary>End a scope <see cref="BeginGpuScope"/> started.</summary>
	public abstract void EndGpuScope( GpuScope scope );

	/// <summary>Merge lightmap then object parameters above the context's, for the draws that follow.</summary>
	public abstract void SetObjectAttributes( ShaderParameters objectAttributes, ShaderParameters lightmap = null );

	/// <summary>Compute-skin a mesh's instances into vertex-cache blocks.</summary>
	public abstract bool DispatchSkinning( GpuMaterial skinningShader, RenderMesh mesh, int modelMesh, GpuBuffer transforms, ReadOnlySpan<int> slots, ReadOnlySpan<int> vertexCacheOffsets, int blendWeights,
		GpuBuffer volumes = null, ReadOnlySpan<int> volumeOffsets = default, ReadOnlySpan<int> volumeCounts = default, GpuBuffer anchors = null, ReadOnlySpan<int> anchorOffsets = default, bool morph = false );

	/// <summary>Record queued morphs, before skinning.</summary>
	public abstract void GenerateMorphs();

	/// <summary>Before skinning: earlier vertex shaders finish reading the cache.</summary>
	public abstract void BarrierVertexCacheToWrite();

	/// <summary>After skinning: the writes are visible to vertex shaders.</summary>
	public abstract void BarrierVertexCacheToRead();

	/// <summary>Bind a draw's vertex-cache id streams, after instance ids.</summary>
	public abstract bool BindVertexCacheIds( RenderMesh mesh, in RenderMesh.Draw draw );

	/// <summary>Draw the 2D sky into the bound target.</summary>
	public abstract bool DrawSky( SceneLighting lighting, StringToken shaderMode );

	/// <summary>Copy HDR to the output, encoding 8-bit targets as sRGB.</summary>
	public abstract void CopyToSwapChain( in RenderOutput output, Rect viewport );

	/// <summary>The final colour target and whether it's written through an sRGB view.</summary>
	public abstract GpuTexture OutputColor( in RenderOutput output, out bool srgbWrite );

	/// <summary>Resolve bound MSAA colour into <see cref="ViewTarget.Resolved"/>.</summary>
	public abstract void Resolve( in RenderOutput output );

	/// <summary>Resolve a multisampled texture output's scratch into the texture.</summary>
	public abstract void ResolveScratch( in RenderOutput output );

	/// <summary>Copy the first array slices or cube faces between equal-sized textures.</summary>
	public abstract void CopyTexture( GpuTexture source, GpuTexture destination, int slices );

	/// <summary>Copy a rectangle of the first slice between textures of the same format.</summary>
	public abstract void CopyTextureRegion( GpuTexture source, GpuTexture destination, int sourceX, int sourceY, int destinationX, int destinationY, int width, int height );

	/// <summary>Make depth writable after pixel-shader reads.</summary>
	public abstract void BarrierToDepthWrite( GpuTexture depth );

	/// <summary>Make depth readable by compute.</summary>
	public abstract void BarrierToComputeRead( GpuTexture depth );

	/// <summary>Make a drawn colour target readable by compute.</summary>
	public abstract void BarrierColorToComputeRead( GpuTexture texture );

	/// <summary>Make a compute-written texture readable by pixel shaders.</summary>
	public abstract void BarrierComputeToPixelShaderRead( GpuTexture texture );

	/// <summary>Make depth readable by pixel shaders.</summary>
	public abstract void BarrierToPixelShaderRead( GpuTexture depth );

	/// <summary>Set the viewport and depth range.</summary>
	public abstract void SetViewport( Rect rect, float minZ = 0, float maxZ = 1 );

	/// <summary>Clear the bound targets.</summary>
	public abstract void Clear( Color color, bool clearColor = true, bool clearDepth = true );

	/// <summary>
	/// Fill a texture with a colour at this point in the work, not bound as a target. By default the device fills it now
	/// (<see cref="GpuDevice.Clear"/>).
	/// </summary>
	public virtual void ClearTexture( GpuTexture texture, Color color ) => RenderHost.Current.Device.Clear( texture, color );

	/// <summary>Set a constant buffer's contents for this frame.</summary>
	public abstract void SetConstants<T>( StringToken name, in T value ) where T : unmanaged;

	/// <summary>Write and bind a buffer from <see cref="CreateConstantBuffer"/>.</summary>
	public abstract void SetConstantBuffer<T>( StringToken name, IntPtr buffer, in T value ) where T : unmanaged;

	/// <summary>
	/// Write the view's descriptor set (<c>g_viewSet</c>) from this frame context's attributes - its fog, lighting config,
	/// sun, clusters, lights, decals, shadows, volumes and light modulators, by their attribute names - and bind it for
	/// everything recorded after, here and on the frame's segments. Once a frame, after all of them are set and before
	/// anything reads them; later calls in the frame do nothing. See docs/managed/static-descriptor-sets.md.
	/// </summary>
	public virtual void WriteViewSet() { }

	/// <summary>Bind a draw's vertex and index buffers.</summary>
	public abstract bool BindGeometry( RenderMesh mesh, in RenderMesh.Draw draw );

	/// <summary>Set a draw's pipeline state for a shader mode.</summary>
	public abstract bool SetRenderState( GpuMaterial material, StringToken shaderMode, RenderMesh mesh, in RenderMesh.Draw draw, int depthBias = 0, float slopeScaledDepthBias = 0, bool stripPixelShader = false, bool vertexCache = false );

	/// <summary>
	/// Draw skinned instances whose vertices come from the vertex cache: the draw's geometry and state with the cache's input
	/// layout, the instance ids, then the cache's vertex ids. A backend can do it in one call that reuses similar-material
	/// state, as <see cref="DrawModel"/> does.
	/// </summary>
	public virtual bool DrawVertexCache( GpuMaterial material, StringToken shaderMode, RenderMesh mesh, in RenderMesh.Draw draw, int firstInstance, int instances, int depthBias = 0, float slopeScaledDepthBias = 0, bool stripPixelShader = false, int textureSize = TextureSizes.All )
	{
		if ( !BindGeometry( mesh, draw ) ) return false;
		if ( !SetRenderState( material, shaderMode, mesh, draw, depthBias, slopeScaledDepthBias, stripPixelShader, vertexCache: true ) ) return false;

		// Instance ids over the model's stream 1, before the cache's vertex ids
		BindInstances( 1, firstInstance );
		BindVertexCacheIds( mesh, draw );
		DrawIndexedInstanced( draw, instances );
		return true;
	}

	/// <summary>
	/// Bind and draw instances in one call, reusing similar-material state. <paramref name="textureSize"/> is the size in texels
	/// the draw's textures appear at, which texture streaming takes as the mips they need (<see cref="TextureSizes"/>).
	/// </summary>
	public abstract bool DrawModel( GpuMaterial material, StringToken shaderMode, RenderMesh mesh, in RenderMesh.Draw draw, int firstInstance, int instances, int depthBias = 0, float slopeScaledDepthBias = 0, bool stripPixelShader = false, bool skinned = false, int textureSize = TextureSizes.All );

	/// <summary>Draw the instances occlusion culling kept, from its arguments.</summary>
	public abstract bool DrawModelCulled( GpuMaterial material, StringToken shaderMode, RenderMesh mesh, in RenderMesh.Draw draw, OcclusionPhase phase, int firstInstance, int argsIndex, int depthBias = 0, float slopeScaledDepthBias = 0, bool stripPixelShader = false, bool skinned = false, int textureSize = TextureSizes.All );

	/// <summary>
	/// One aggregate's visible fragments for <see cref="DrawGpuDriven"/>: its arguments are <paramref name="Count"/> of the
	/// frame's from <paramref name="FirstArg"/>, its fragments' instances in the transform buffer start at
	/// <paramref name="FirstInstance"/>, and <paramref name="ObjectAttributes"/> are what it would set as object attributes.
	/// <paramref name="Owner"/> is the aggregate, whose fragments (by their arguments' first instance) don't change.
	/// With <paramref name="Instances"/>, it's a mesh draw's <paramref name="Count"/> instances from
	/// <paramref name="FirstInstance"/> instead, each the whole draw, which the caller has already culled: no arguments, no owner.
	/// With a <see cref="ResidentMeshes"/> owner, it's draw <paramref name="Draw"/> of LOD <paramref name="Lod"/> for each of the
	/// group's <paramref name="Count"/> meshes, which the GPU culls, keeping each only where it picks that LOD for it; with
	/// <paramref name="MeshLighting"/>, each mesh's lighting attributes (<paramref name="ObjectAttributes"/> is the first's,
	/// and the rest differ from it only in their lighting records, <see cref="GpuDevice.LightingFreeKey"/>). An aggregate in a
	/// resident list of casters whose settling decides (<see cref="GpuDrivenCull.Filter"/>) has its <paramref name="Moved"/>
	/// word (<see cref="MovedWord"/>) for every fragment; <see cref="MovedNone"/> keeps it in every view.
	/// </summary>
	internal readonly record struct GpuDrivenBatch( GpuMaterial Material, RenderMesh Mesh, RenderMesh.Draw Draw, ShaderParameters ObjectAttributes, bool StripPixelShader, int FirstArg, int Count, int FirstInstance, long Triangles, object Owner, bool Instances = false, int TextureSize = TextureSizes.All, int Lod = 0, ShaderParameters[] MeshLighting = null, uint Moved = MovedNone );

	/// <summary>A moved word for a static caster, or for none: kept by every filter that keeps static casters.</summary>
	internal const uint MovedNone = uint.MaxValue;

	/// <summary>
	/// A caster's moved stamp (<see cref="RenderWorld.MovedStamps"/>) as the GPU compares it with a view's
	/// <see cref="GpuDrivenCull.SettledStamp"/>: its frame in the low 31 bits, bit 31 when it's restless; <see cref="MovedNone"/>
	/// for a static one.
	/// </summary>
	internal static uint MovedWord( ulong moved, bool isStatic ) => isStatic ? MovedNone : StampWord( moved & ~Shadows.SettledCasters.Restless ) | ((moved & Shadows.SettledCasters.Restless) != 0 ? 0x80000000u : 0);

	/// <summary>A settled stamp as the GPU compares it (31 bits).</summary>
	internal static uint StampWord( ulong stamp ) => (uint)Math.Min( stamp, 0x7ffffffeul );

	/// <summary>
	/// Which casters of a resident list a shadow view keeps by when each last moved, as the collect's settled test does: the
	/// sun's far cascade cache takes static ones and those settled when it was drawn (for a baked sun only the settled ones
	/// that aren't static), and what's drawn over it the rest that aren't static.
	/// </summary>
	internal enum ResidentFilter
	{
		None,
		Settled,
		SettledNoStatic,
		Unsettled,
	}

	/// <summary>
	/// Resident meshes of one model that draw alike (a <see cref="GpuDrivenBatch"/>'s owner): what the GPU culls them
	/// and picks each one's LOD by. Never changed once made; a change makes another.
	/// </summary>
	internal sealed class ResidentMeshes
	{
		/// <summary>Each mesh's world bounds, transform slot, and LOD scale (its largest scale, or -1 - LOD for a fixed LOD).</summary>
		public readonly Vector3[] Centers, Extents;
		public readonly int[] Slots;
		public readonly float[] LodScales;

		/// <summary>The model's LOD count and switch distances (<see cref="RenderMesh.LodForScreenSize(float, float)"/>).</summary>
		public readonly int LodCount;
		public readonly float[] SwitchDistances;

		/// <summary>Per LOD and draw: what names its visibility, the same in every view and pass, and its records when tinted.</summary>
		public readonly object[][] VisibilityKeys;

		/// <summary>
		/// A model with draw tints: each mesh's slot is the first of a kept run of an entry per draw of every LOD, a LOD's
		/// first at <see cref="TintOffsets"/>, so each draw has records of its own.
		/// </summary>
		public readonly bool Tinted;
		public readonly int[] TintOffsets;

		/// <summary>Switch distances the GPU reads (index 0 is never read): a model with more isn't resident.</summary>
		public const int MaxSwitchDistances = 8;

		/// <summary>
		/// Each mesh's lightmap and light probe attributes in main views, when they aren't all the same object: each mesh then
		/// draws with its own lighting record. Null where every mesh has the same (or none).
		/// </summary>
		public readonly ShaderParameters[] Lightmaps, Probes;

		/// <summary>
		/// In a list of casters whose settling decides (<see cref="GpuDrivenCull.Filter"/>): each mesh's moved word
		/// (<see cref="MovedWord"/>); null in every other list.
		/// </summary>
		public readonly uint[] Moved;

		public int Count => Slots.Length;

		public ResidentMeshes( RenderMesh mesh, Vector3[] centers, Vector3[] extents, int[] slots, float[] lodScales, ShaderParameters[] lightmaps = null, ShaderParameters[] probes = null, uint[] moved = null )
		{
			Centers = centers;
			Extents = extents;
			Slots = slots;
			LodScales = lodScales;
			Lightmaps = lightmaps;
			Probes = probes;
			Moved = moved;
			LodCount = mesh.LodCount;
			SwitchDistances = mesh.SwitchDistances.ToArray();
			Tinted = mesh.HasDrawTints;
			TintOffsets = new int[LodCount];
			for ( int lod = 0; lod < LodCount; lod++ ) TintOffsets[lod] = Tinted ? mesh.TintOffsetForLod( lod ) : 0;
			VisibilityKeys = new object[LodCount][];
			for ( int lod = 0; lod < LodCount; lod++ )
			{
				VisibilityKeys[lod] = new object[mesh.DrawsForLod( lod ).Length];
				for ( int d = 0; d < VisibilityKeys[lod].Length; d++ ) VisibilityKeys[lod][d] = new object();
			}
		}
	}

	/// <summary>Whether <see cref="DrawGpuDriven"/> draws anything on this device.</summary>
	internal virtual bool GpuDriven => false;

	/// <summary>
	/// Whether a draw census is counting what this context records (<c>borealislab -drawcensus</c>, debug only): features then
	/// say what each draw is in <see cref="CensusNote"/>, and the frame names its scopes by view. Off, nothing is counted.
	/// </summary>
	internal bool Census;

	/// <summary>While <see cref="Census"/>: what the next model draw is and why it isn't GPU-driven, which the census takes.</summary>
	internal string CensusNote;

	/// <summary>
	/// Before a frame's segments record: get <paramref name="view"/>'s state for <see cref="DrawGpuDriven"/>'s
	/// visibility-driven pass ready (cleared once, here, for every call that then writes into it).
	/// </summary>
	internal virtual void BeginGpuDrivenView( object view )
	{
	}

	/// <summary>
	/// In setup, after every view has prepared: cull the views' resident lists (<see cref="GpuDrivenCull.Plan"/>) on the GPU
	/// for the calls that draw them this frame, all at once, where the device can.
	/// </summary>
	internal virtual void CullResident( ReadOnlySpan<GpuDrivenCull> views )
	{
	}

	/// <summary>
	/// The view's culling for <see cref="DrawGpuDriven"/> to repeat on the GPU over every fragment of an aggregate (the
	/// same frustum and size tests the CPU makes): the frustum's six planes, the camera and squared size threshold
	/// (<see cref="Culling.SizeCull"/>), and whether it's a shadow view (fragments that cast no shadow drop out).
	/// </summary>
	internal struct GpuDrivenCull
	{
		public Vector4 Left, Right, Bottom, Top, Near, Far;
		public Vector4 CameraAndThreshold;
		public bool Shadow;

		/// <summary>
		/// For <see cref="ResidentMeshes"/>: whether they skip the size test (a static shadow cache, kept while the camera
		/// moves), and how their LODs are picked: the root view's camera and tangent of half its field of view, and its
		/// screen width; or, with <see cref="LodTexels"/>, <see cref="LodWidth"/> is the view's texels per unit.
		/// </summary>
		public bool KeepsSmallMeshes;
		public Vector4 LodCamera;
		public float LodWidth;
		public bool LodTexels;

		/// <summary>
		/// Which of its resident list's casters it keeps by when each last moved, against the stamps the far cascade's cache was
		/// drawn at (<see cref="StampWord"/>, as <see cref="Shadows.SettledCasters.SettledBy"/> compares them). The view's own
		/// batches are kept.
		/// </summary>
		public ResidentFilter Filter;
		public uint SettledStamp, RestlessStamp;

		/// <summary>
		/// The view, which the visibility-driven forward pass keeps its ids and lists for; and whether this is its depth
		/// prepass (it may write ids) or a pass that may draw only what owns a sample of them.
		/// </summary>
		public object View;
		public bool Ids, Visibility;

		/// <summary>
		/// With <see cref="GpuDevice.DrawsAggregatesResident"/>: the caller's list of resident batches, the same object every
		/// frame while <see cref="PlanVersion"/> is the same, so the device may keep what it makes of them (buckets, records,
		/// multi-draws) between frames and views of the same kind. Null for a call of only the view's own batches.
		/// </summary>
		public object Plan;
		public int PlanVersion;

		/// <summary>How many of the call's first batches are <see cref="Plan"/>'s; the view's own follow them.</summary>
		public int PlanBatches;
	}

	/// <summary>
	/// Draw a layer's aggregate batches GPU-driven: per-draw data in tables and one multi-draw per pipeline, where the device
	/// can. Sets <paramref name="drawn"/> for each batch drawn; the caller draws the rest as before. Devices without a
	/// GPU-driven path draw none. With <paramref name="cull"/>, a device may cull every fragment of each batch's aggregate
	/// on the GPU instead of drawing the batch's arguments.
	/// </summary>
	internal virtual void DrawGpuDriven( StringToken shaderMode, ReadOnlySpan<GpuDrivenBatch> batches, ReadOnlySpan<GpuBuffer.IndirectDrawIndexedArguments> args, TransformBuffer transforms, int depthBias, float slopeScaledDepthBias, Span<bool> drawn, in GpuDrivenCull? cull = null )
	{
	}

	/// <summary>Draw an aggregate's fragments, one indirect draw each.</summary>
	public abstract bool DrawModelFragments( GpuMaterial material, StringToken shaderMode, RenderMesh mesh, in RenderMesh.Draw draw, int firstInstance, GpuBuffer args, int firstArg, int count, int depthBias = 0, float slopeScaledDepthBias = 0, bool stripPixelShader = false, int textureSize = TextureSizes.All );

	/// <summary>Draw the aggregate fragments occlusion culling kept.</summary>
	public abstract bool DrawModelFragmentsCulled( GpuMaterial material, StringToken shaderMode, RenderMesh mesh, in RenderMesh.Draw draw, OcclusionPhase phase, int firstInstance, int cullArg, int count, int depthBias = 0, float slopeScaledDepthBias = 0, bool stripPixelShader = false, int textureSize = TextureSizes.All );

	/// <summary>Copy data into a buffer, ordered before later draws.</summary>
	public void UploadBuffer<T>( GpuBuffer buffer, ReadOnlySpan<T> data ) where T : unmanaged => UploadBuffer( buffer, data, 0 );

	/// <summary>Copy data into a buffer from <paramref name="offsetBytes"/> in, ordered before later draws.</summary>
	public abstract void UploadBuffer<T>( GpuBuffer buffer, ReadOnlySpan<T> data, int offsetBytes ) where T : unmanaged;

	/// <summary>Bind instance ids at <paramref name="firstInstance"/>.</summary>
	public abstract void BindInstances( int stream, int firstInstance );

	/// <summary>Order a buffer's accesses.</summary>
	public abstract void BufferBarrier( GpuBuffer buffer, GpuStage srcStage, GpuAccess srcAccess, GpuStage dstStage, GpuAccess dstAccess );

	/// <summary>Read part of a buffer back once the GPU has written it. The callback runs later, on any thread.</summary>
	public abstract void ReadBuffer( GpuBuffer buffer, int offset, int bytes, GpuReadback callback );

	/// <summary>Allow one lazy frame copy for this translucent layer.</summary>
	public abstract void BeginFrameBufferReads( GpuTexture copy, Rect viewport );

	/// <summary>Stop frame copies after the translucent layer.</summary>
	public abstract void EndFrameBufferReads();

	/// <summary>Copy the frame on its first reader in the layer.</summary>
	public abstract void CopyFrameBuffer();

	/// <summary>Dispatch a compute material with this context's parameters.</summary>
	public abstract void Dispatch( GpuMaterial computeShader, int x, int y, int z );

	/// <summary>Dispatch a compute material with its own parameters.</summary>
	public abstract void Dispatch( GpuMaterial computeShader, ShaderParameters parameters, int x, int y, int z );

	/// <summary>Draw a material over the bound target's viewport, with its own parameters and a shader mode.</summary>
	public abstract void DrawFullscreen( GpuMaterial material, ShaderParameters parameters, StringToken shaderMode );

	/// <summary>Order accesses to a texture, leaving it in <paramref name="layout"/>.</summary>
	public abstract void TextureBarrier( GpuTexture texture, GpuStage srcStage, GpuAccess srcAccess, GpuStage dstStage, GpuAccess dstAccess, TextureLayout layout );

	/// <summary>Draw a draw call's indices with the state already bound.</summary>
	public abstract void DrawIndexedInstanced( in RenderMesh.Draw draw, int instanceCount );
}

/// <summary>
/// Receives a buffer read back from the GPU (<see cref="GpuContext.ReadBuffer"/>).
/// </summary>
internal delegate void GpuReadback( Span<byte> data );

/// <summary>
/// How depth prepasses treat overlay (viewmodel) pixels in the stencil.
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
