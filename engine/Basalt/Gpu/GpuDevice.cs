namespace Basalt;

/// <summary>
/// The graphics device Basalt renders with: it makes and loads resources, and records and submits work. A host supplies
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

	/// <summary>Whether the device has an async compute queue.</summary>
	public abstract bool SupportsAsyncCompute { get; }

	/// <summary>Whether the device can take an indirect draw count from a buffer.</summary>
	public abstract bool SupportsDrawIndirectCount { get; }

	/// <summary>Allocate vertex-cache space this frame and return its first vertex.</summary>
	public abstract int AllocateVertexCache( int vertices );

	/// <summary>Where a queued morph is in the atlas once generated: u offset, u range, v offset, v range.</summary>
	public abstract Vector4 MorphSubrect( int handle );

	/// <summary>Start a frame's submission, naming the swap chain it ends up in for its GPU timing.</summary>
	public abstract void BeginSubmitting( GpuSwapChain swapChain );

	/// <summary>Tag the next submissions, so a profiler can pair GPU work with the frame that made it; 0 for none.</summary>
	public abstract void SetSubmitTag( ulong tag );

	/// <summary>A render system has submitted everything it recorded for a frame: send on whatever the device is holding to submit together.</summary>
	public virtual void EndSubmitting() { }

	/// <summary>The time shaders see this frame.</summary>
	public abstract float RenderTime { get; }

	/// <summary>A zeroed constant buffer beyond per-frame limits. Free with <see cref="DestroyConstantBuffer"/>.</summary>
	public abstract IntPtr CreateConstantBuffer( int size );

	/// <summary>Free a buffer from <see cref="CreateConstantBuffer"/>.</summary>
	public abstract void DestroyConstantBuffer( IntPtr buffer );

	/// <summary>Samples per pixel of a swap chain's colour buffer.</summary>
	public abstract int SwapChainSamples( GpuSwapChain swapChain );
}
