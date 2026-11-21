namespace Borealis;

/// <summary>
/// The graphics device Borealis renders with: it makes and loads resources, and records and submits work. A host supplies
/// one through <see cref="RenderHost.Device"/>.
/// </summary>
public abstract class GpuDevice
{
	/// <summary>
	/// Create a texture.
	/// </summary>
	public abstract GpuTexture CreateTexture( in TextureDesc desc, string name );

	/// <summary>
	/// Load a texture asset by path, or null if it doesn't exist.
	/// </summary>
	public abstract GpuTexture LoadTexture( string path );

	/// <summary>
	/// A 1x1 black texture.
	/// </summary>
	public abstract GpuTexture Black { get; }

	/// <summary>
	/// The bindless index shaders read for a missing texture, through its sRGB or linear 2D view.
	/// </summary>
	public abstract int MissingTextureViewIndex( bool srgb );

	/// <summary>
	/// Fill a texture with a colour now, outside any recorded work.
	/// </summary>
	public abstract void Clear( GpuTexture texture, Color color );

	/// <summary>
	/// Create the device's side of a <see cref="GpuBuffer"/>.
	/// </summary>
	public abstract IDisposable CreateBuffer( int elementCount, int elementSize, GpuBuffer.UsageFlags usage, string name );

	/// <summary>
	/// Copy bytes into a buffer now, outside any recorded work.
	/// </summary>
	public abstract void SetData( GpuBuffer buffer, ReadOnlySpan<byte> data );

	/// <summary>
	/// An empty set of shader parameters.
	/// </summary>
	public abstract ShaderParameters CreateParameters();

	/// <summary>
	/// The sprite sheets' metadata, which shaders that sample sheets read.
	/// </summary>
	public abstract GpuTexture SheetMetadata { get; }

	/// <summary>
	/// Load a material asset by path, or null if it doesn't exist. Main thread only.
	/// </summary>
	public abstract GpuMaterial LoadMaterial( string path );

	/// <summary>
	/// The shared material for a shader with default values. Don't release it. Main thread only.
	/// </summary>
	public abstract GpuMaterial MaterialFromShader( string shaderPath );

	/// <summary>
	/// A new material for a shader, owned by the caller. Main thread only, outside rendering.
	/// </summary>
	public abstract GpuMaterial CreateMaterial( string name, string shaderPath );

	/// <summary>
	/// A frame context, or a segment context recording one of <paramref name="frame"/>'s segments.
	/// </summary>
	internal abstract Gpu.GpuContext CreateContext( Gpu.GpuContext frame = null );

	/// <summary>
	/// Whether this device's contexts draw aggregates GPU-driven (<see cref="Gpu.GpuContext.DrawGpuDriven"/>), so a layer's
	/// aggregates record as one run rather than spread over workers.
	/// </summary>
	internal virtual bool DrawsAggregatesGpuDriven => false;

	/// <summary>
	/// Whether, drawing aggregates GPU-driven, it also culls their fragments on the GPU, so preparing a view needn't cull or
	/// size them fragment by fragment (a worker sizes them for texture streaming instead).
	/// </summary>
	internal virtual bool CullsAggregatesGpuDriven => false;

	/// <summary>
	/// Whether, culling aggregates on the GPU, it keeps what a call of every aggregate makes between frames
	/// (<see cref="Gpu.GpuContext.GpuDrivenCull.Plan"/>): views then hand it the world's aggregates as one unchanging list
	/// instead of preparing their own, and pay per frame only for the view (docs/reference/borealis-gpu-driven.md, "Resident
	/// aggregates").
	/// </summary>
	internal virtual bool DrawsAggregatesResident => false;

	/// <summary>
	/// Whether, drawing aggregates resident, static meshes join their lists too (<see cref="Gpu.GpuContext.ResidentMeshes"/>):
	/// the GPU culls them and picks their LODs, and views don't prepare them.
	/// </summary>
	internal virtual bool DrawsMeshesResident => false;

	/// <summary>
	/// Whether, drawing meshes resident, a map's static overlays join main views' lists too, a list per render order culled
	/// on the GPU with its order kept (<see cref="Gpu.GpuContext.GpuDrivenCull.KeepsOrder"/>), and views don't prepare them.
	/// </summary>
	internal virtual bool DrawsOverlaysResident => false;

	/// <summary>
	/// Whether, drawing aggregates resident, the sun's far cascade cache and what's drawn over it take resident lists of the
	/// casters that aren't static too, which the GPU takes by when each last moved (<see cref="Gpu.GpuContext.ResidentFilter"/>).
	/// </summary>
	internal virtual bool DrawsSettledResident => false;

	/// <summary>
	/// Whether a resident mesh's draws with this material can be drawn GPU-driven in every pass (its shader has GPU-driven
	/// variants): a draw the device leaves would go undrawn, so a mesh with one isn't made resident.
	/// </summary>
	internal virtual bool TakesResident( GpuMaterial material ) => false;

	/// <summary>
	/// Raised each time a program the device was compiling in the background becomes ready: what <see cref="TakesResident"/>
	/// and <see cref="TakesOverlay"/> said of its materials meanwhile is looked at again.
	/// </summary>
	internal virtual int ProgramsVersion => 0;

	/// <summary>
	/// Whether a static overlay's draws with this material draw GPU-driven in the forward pass, blended or not: a resident
	/// overlay's draw the device leaves would go undrawn, and a view's own that it leaves draws after its render order's call.
	/// </summary>
	internal virtual bool TakesOverlay( GpuMaterial material ) => false;

	/// <summary>
	/// What GPU-driven drawing buckets these object attributes by, leaving out what each draw's lighting record holds (its
	/// baked lighting and probe volume constants): resident meshes whose lighting attributes have the same key share a group,
	/// each with its own lighting record. Without GPU-driven drawing, each attributes object is its own.
	/// </summary>
	internal virtual long LightingFreeKey( ShaderParameters attributes ) => attributes is null ? 0 : (uint)System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode( attributes ) | 1L << 62;

	/// <summary>
	/// A resident owner (<see cref="Gpu.GpuContext.ResidentMeshes"/>) no list draws any more: the device frees its records once
	/// the frames that could read them are done.
	/// </summary>
	internal virtual void ReleaseResident( object owner )
	{
	}

	/// <summary>
	/// A view (<see cref="GpuContext.GpuDrivenCull.View"/>) that won't draw again, from its owner as it disposes it: the device
	/// forgets what it kept of the view's draws, which otherwise holds the view and what it last drew.
	/// </summary>
	internal virtual void ReleaseView( object view )
	{
	}

	/// <summary>
	/// Whether it streams textures by the sizes draws ask for, so what draws without a size of its own should give one
	/// through <see cref="AddTextureSizes"/>.
	/// </summary>
	internal virtual bool StreamsTextures => false;

	/// <summary>Take texture sizes from <paramref name="source"/> every frame, for streaming; nothing on a device that doesn't stream.</summary>
	internal virtual void AddTextureSizes( ITextureSizeSource source )
	{
	}

	/// <summary>Whether static meshes' opaque draws join the aggregates' GPU-driven call (<see cref="DrawsAggregatesGpuDriven"/> too).</summary>
	internal virtual bool DrawsMeshesGpuDriven => false;

	/// <summary>
	/// Whether skinned meshes' opaque draws join them too (<see cref="DrawsMeshesGpuDriven"/> as well): their instances' base
	/// entries as transforms, drawn from the vertex cache or skinned in the vertex shader as their classic draws are.
	/// </summary>
	internal virtual bool DrawsSkinnedGpuDriven => false;

	/// <summary>
	/// Whether fading objects' draws (the faded list: dithered opaque, <c>D_OPAQUE_FADE</c>) draw GPU-driven, a call per list
	/// range, as the opaque list's do (<see cref="DrawsMeshesGpuDriven"/> as well; skinned ones with <see cref="DrawsSkinnedGpuDriven"/>).
	/// </summary>
	internal virtual bool DrawsFadedGpuDriven => false;

	/// <summary>
	/// Whether the translucent list's draws draw GPU-driven (<see cref="DrawsMeshesGpuDriven"/> as well): in colour layers in
	/// their sorted order (<see cref="GpuContext.GpuDrivenInOrder"/>), in shadows as the faded list's.
	/// </summary>
	internal virtual bool DrawsTranslucentGpuDriven => false;

	/// <summary>Whether a map's static overlays draw GPU-driven, a call per render order, regrouped by pipeline within it.</summary>
	internal virtual bool DrawsOverlaysGpuDriven => false;

	/// <summary>
	/// Whether its materials say if their draws need a stencil (<see cref="GpuMaterial.NeedsStencil"/>), so views can draw
	/// into depth without one until something needs it (<see cref="RenderSystem.DepthFormat"/>). Off, every view's depth
	/// has a stencil.
	/// </summary>
	internal virtual bool KnowsStencilUse => false;

	/// <summary>
	/// Whether its opaque pass writes surfaces' normals and roughness into a second target
	/// (<see cref="StageTarget.Normals"/>), which screen-space reflections trace from. Off, a frame traces none.
	/// </summary>
	internal virtual bool WritesForwardNormals => false;

	/// <summary>
	/// Whether a view's depth prepass leaves ids that each pixel's triangle can be found from, and the device resolves them
	/// into normals after it (<see cref="GpuContext.ResolveIdsNormals"/>, <see cref="IdsNormalsLayer"/>), which ambient
	/// occlusion reads instead of rebuilding normals from depth.
	/// </summary>
	internal virtual bool ResolvesIdsNormals => false;

	/// <summary>
	/// The error a generated level may have, in pixels at an object's nearest point, as views pick the levels of meshes they
	/// draw themselves (<see cref="GpuMesh.LevelsFor"/>); 0 when the device makes none.
	/// </summary>
	internal virtual float LevelPixels => 0;

	/// <summary>Counts generated levels published (<see cref="GpuMesh.LevelsFor"/>): a mesh waiting for some asks again once it moves.</summary>
	internal virtual int LevelsVersion => 0;

	/// <summary>Whether the device has an async compute queue.</summary>
	public abstract bool SupportsAsyncCompute { get; }

	/// <summary>Whether the device can take an indirect draw count from a buffer.</summary>
	public abstract bool SupportsDrawIndirectCount { get; }

	/// <summary>Allocate vertex-cache space this frame and return its first vertex.</summary>
	public abstract int AllocateVertexCache( int vertices );

	/// <summary>Where a queued morph is in the atlas once generated: u offset, u range, v offset, v range.</summary>
	public abstract Vector4 MorphSubrect( int handle );

	/// <summary>A render system has submitted everything it recorded for a frame: send on whatever the device is holding to submit together.</summary>
	public virtual void EndSubmitting() { }

	/// <summary>The time shaders see this frame.</summary>
	public abstract float RenderTime { get; }

	/// <summary>A zeroed constant buffer beyond per-frame limits. Free with <see cref="DestroyConstantBuffer"/>.</summary>
	public abstract IntPtr CreateConstantBuffer( int size );

	/// <summary>Free a buffer from <see cref="CreateConstantBuffer"/>.</summary>
	public abstract void DestroyConstantBuffer( IntPtr buffer );
}
