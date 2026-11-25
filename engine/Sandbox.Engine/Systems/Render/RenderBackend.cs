using NativeEngine;
using Sandbox.Rendering;

namespace Sandbox;

/// <summary>
/// The device under <see cref="Graphics"/>: what its statics, <see cref="Rendering.CommandList"/>, compute shaders and
/// GPU buffers record with. Each render block (<see cref="Graphics.Scope"/>) draws on one; outside a block, buffer
/// uploads and readbacks go to <see cref="Default"/>: Borealis.Gpu's (<c>BorealisGpuRenderBackend</c>), or none
/// (<see cref="HeadlessRenderBackend"/>). Callers validate their arguments before they get here, so a backend only lowers the call.
/// </summary>
internal abstract class RenderBackend
{
	/// <summary>
	/// The process's device: a Borealis.Gpu device's with <c>-borealisgpu</c> (<see cref="Graphics.Initialize"/>), else none
	/// (<see cref="HeadlessRenderBackend"/>).
	/// </summary>
	public static RenderBackend Default { get; private set; } = HeadlessRenderBackend.Instance;

	/// <summary>
	/// Make <paramref name="backend"/> the process's device, before anything renders; null goes back to none.
	/// </summary>
	internal static void SetDefault( RenderBackend backend ) => Default = backend ?? HeadlessRenderBackend.Instance;

	//
	// Blocks and resources
	//

	/// <summary>
	/// Start a render block with no view on this device (<see cref="Graphics.Scope.CreateOn"/>): the
	/// attributes it starts with.
	/// </summary>
	public abstract RenderAttributes BeginBlock();

	/// <summary>Submit what the block recorded.</summary>
	public abstract void EndBlock();

	/// <summary>
	/// A block for work the game asks for outside one, which runs at once on Source 2's device (a compute dispatch, a texture
	/// copy, mips, painting into a texture): a block of its own, submitted on disposal, unless the device records it with
	/// what it uploads before the next block (Borealis.Gpu).
	/// </summary>
	internal virtual Graphics.Scope BeginUpdateWork() => Graphics.Scope.CreateOn( this );

	/// <summary>
	/// Whether update-time work recorded on this thread (painting into a texture) runs later, through
	/// <see cref="DeferUpdateWork"/>: on the main thread while a render thread draws a frame on this device (Borealis.Gpu).
	/// </summary>
	internal virtual bool DefersUpdateWork => false;

	/// <summary>
	/// Run <paramref name="work"/>, which opens update-time work of its own (<see cref="BeginUpdateWork"/>), once the frame the
	/// render thread draws is drawn, in order with this thread's other writes to the device meanwhile.
	/// </summary>
	internal virtual void DeferUpdateWork( Action work ) => work();

	/// <summary>
	/// On the main thread, before taking a lock the render thread also takes, when what follows under it may wait for the
	/// drawing (a compute dispatch outside a block): wait for the frame the render thread draws now, so nothing waits for it
	/// with the lock held. Only the main thread hands frames over, so none starts before the lock is let go.
	/// </summary>
	internal virtual void FinishDrawing() { }

	/// <summary>A texture's bindless index on this device: its linear view; the sRGB view is the next.</summary>
	public abstract int TextureIndex( Texture texture );

	/// <summary>A sampler state's bindless index on this device.</summary>
	public abstract int SamplerIndex( SamplerState state );

	/// <summary>
	/// What uploads are counted against: copies made under one key are visible to later draws under it.
	/// </summary>
	public abstract IntPtr UploadContext { get; }

	/// <summary>Forget what this device holds for an engine texture or buffer that's being disposed.</summary>
	public virtual void Release( object resource ) { }

	/// <summary>
	/// Read a rectangle of one mip and slice of a texture, as this device holds it, into <paramref name="data"/> as
	/// <paramref name="format"/> at <paramref name="destination"/>, rows <paramref name="stride"/> bytes apart (0 packs
	/// them). Waits for the GPU. False when it can't.
	/// </summary>
	public abstract bool ReadPixels( Texture texture, NativeRect source, int slice, int mip, NativeRect destination, IntPtr data, ImageFormat format, int stride );

	/// <summary>
	/// Read bytes of a buffer from a byte offset, once the GPU has written them. Waits for the GPU.
	/// </summary>
	public virtual void ReadBuffer( GpuBuffer buffer, Span<byte> data, int offset ) => throw new NotSupportedException( $"{GetType().Name} can't read buffers back." );

	/// <summary>
	/// The first <paramref name="most"/> bytes of a compiled resource (<c>_c</c> appended unless it's there) as this
	/// device finds its content, or null.
	/// </summary>
	public virtual byte[] ReadCompiledPrefix( string path, long most ) => null;

	/// <summary>Where this device's sheet cache places sprite sheets (<see cref="SheetData"/>).</summary>
	protected readonly SheetCache Sheets = new();

	/// <summary>
	/// <paramref name="texture"/>'s sheet data (<see cref="Texture.SequenceData"/>): where its sheet is in this device's sheet
	/// cache, placed there the first time it's asked for. Zero for a texture with no sheet. Any thread.
	/// </summary>
	public virtual Vector4 SheetData( Texture texture ) => Sheets.SheetData( texture );

	// The devices that hold backings, replaced whole when one comes or goes (under the lock): what's called for each of them
	// is called on a copy taken without the lock, so no thread holds it while a device waits (the main thread waits for the
	// render thread's drawing in a device's Update; the render thread releases textures through ReleaseEverywhere)
	static readonly System.Threading.Lock othersLock = new();
	static volatile RenderBackend[] others = [];

	/// <summary>Devices other than <see cref="Default"/> that hold backings, to release them from.</summary>
	protected static void Register( RenderBackend backend )
	{
		lock ( othersLock ) others = [.. others, backend];
	}

	protected static void Unregister( RenderBackend backend )
	{
		lock ( othersLock ) others = Array.FindAll( others, b => b != backend );
	}

	/// <summary>Whether any device other than <see cref="Default"/> exists, which might make copies of engine resources.</summary>
	internal static bool OthersExist => others.Length > 0;

	/// <summary>
	/// A shader was compiled while running (<see cref="Shader.CompileAsync(string, string, System.Threading.CancellationToken)"/>,
	/// a hotloaded <c>.shader</c>): take its compiled file under the path its materials name, replacing what this device had
	/// for it.
	/// </summary>
	public virtual void ShaderCompiled( string path, byte[] compiled ) { }

	/// <summary>Whether any device needs <see cref="ShaderCompiled"/>: a Borealis.Gpu one.</summary>
	internal static bool TakesCompiledShaders => Default is BorealisGpuRenderBackend || others.Length > 0;

	/// <summary>Give a shader compiled while running to every device (<see cref="ShaderCompiled"/>).</summary>
	internal static void ShaderCompiledEverywhere( string path, byte[] compiled )
	{
		Default.ShaderCompiled( path, compiled );
		foreach ( var backend in others )
		{
			if ( backend != Default ) backend.ShaderCompiled( path, compiled );
		}
	}

	/// <summary>
	/// An engine texture's pixels changed (<c>Texture.Update</c>): take the same bytes into this device's copy, if it has one.
	/// False when it can't, and the copy is released instead (made again from the texture when next used).
	/// </summary>
	public virtual bool Update( Texture texture, ReadOnlySpan<byte> data, int x, int y, int z, int width, int height, int depth ) => false;

	/// <summary>Update an engine texture's copies on every other device, releasing those that can't take the bytes.</summary>
	internal static void UpdateEverywhere( Texture texture, ReadOnlySpan<byte> data, int x, int y, int z, int width, int height, int depth )
	{
		foreach ( var backend in others )
		{
			if ( !backend.Update( texture, data, x, y, z, width, height, depth ) ) backend.Release( texture );
		}
	}

	/// <summary>
	/// An engine texture was cleared to one colour (<see cref="Texture.Clear"/>): clear this device's copy, if it has one.
	/// False when it can't, and the copy is released instead (made again from the colour when next used).
	/// </summary>
	public virtual bool Cleared( Texture texture, Color color ) => false;

	/// <summary>Clear an engine texture's copies on every other device, releasing those that can't be cleared.</summary>
	internal static void ClearEverywhere( Texture texture, Color color )
	{
		foreach ( var backend in others )
		{
			if ( !backend.Cleared( texture, color ) ) backend.Release( texture );
		}
	}

	/// <summary>Release an engine resource's backings on every other device.</summary>
	internal static void ReleaseEverywhere( object resource )
	{
		foreach ( var backend in others ) backend.Release( resource );
	}

	//
	// Targets and viewport
	//

	/// <summary>Where draws land, in the bound target's pixels, with the depth range. Set as a rect, it keeps the range.</summary>
	public abstract RenderViewport GetViewport();
	public abstract void SetViewport( in RenderViewport viewport );
	public abstract void SetViewport( Rect rect );

	/// <summary>Bind a render target's colour in a block with no view (<see cref="Graphics.RenderTarget"/>).</summary>
	public abstract void BindRenderTarget( RenderTarget target );

	/// <summary>Clear what's bound.</summary>
	public abstract void Clear( Color color, bool clearColor, bool clearDepth, bool clearStencil );

	/// <summary>Fill a whole texture with one colour.</summary>
	public abstract void ClearTexture( Texture texture, Color color );

	//
	// Draws
	//

	/// <summary>
	/// Draw vertices of <paramref name="vertexType"/> from the CPU, with optional 16-bit indices
	/// (<see cref="Graphics.Draw(Span{Vertex}, int, Material, RenderAttributes, Graphics.PrimitiveType)"/>).
	/// </summary>
	public abstract void DrawVertices( Type vertexType, int vertexSize, IntPtr vertices, int vertexCount, IntPtr indices, int indexCount, Material material, RenderAttributes attributes, Graphics.PrimitiveType primitiveType );

	/// <summary>
	/// Set up a material and attributes for the buffer draws after it, reading vertices of
	/// <paramref name="vertexType"/> (null for none). False when the material can't draw here, and nothing should.
	/// </summary>
	public abstract bool SetRenderState( Material material, RenderAttributes attributes, Type vertexType );

	/// <summary>Buffer draws, after <see cref="SetRenderState"/>.</summary>
	public abstract void Draw( GpuBuffer vertexBuffer, Graphics.PrimitiveType primitiveType, int startVertex, int vertexCount );
	public abstract void DrawIndexed( GpuBuffer vertexBuffer, GpuBuffer indexBuffer, Graphics.PrimitiveType primitiveType, int startIndex, int indexCount );
	public abstract void DrawIndexedInstanced( GpuBuffer indexBuffer, Graphics.PrimitiveType primitiveType, int instanceCount );

	/// <summary>Indirect buffer draws, after <see cref="SetRenderState"/>. <paramref name="vertexBuffer"/> may be null.</summary>
	public abstract void DrawInstancedIndirect( GpuBuffer vertexBuffer, Graphics.PrimitiveType primitiveType, GpuBuffer indirectBuffer, uint bufferOffset, uint drawCount, uint stride );
	public abstract void DrawIndexedInstancedIndirect( GpuBuffer vertexBuffer, GpuBuffer indexBuffer, Graphics.PrimitiveType primitiveType, GpuBuffer indirectBuffer, uint bufferOffset, uint drawCount, uint stride );

	//
	// Models
	//

	/// <summary>Instances of a model at transforms from the CPU; <paramref name="lodLevel"/> null draws native's default.</summary>
	public abstract void DrawModel( Model model, ReadOnlySpan<Transform> transforms, int? lodLevel, RenderAttributes attributes );

	/// <summary><paramref name="count"/> instances of a model, which find their own transforms.</summary>
	public abstract void DrawModel( Model model, int count, RenderAttributes attributes );

	/// <summary>Instances of a model counted by indirect arguments, optionally with transforms from a buffer.</summary>
	public abstract void DrawModelIndirect( Model model, GpuBuffer arguments, int argumentsOffset, RenderAttributes attributes );
	public abstract void DrawModelIndirect( Model model, GpuBuffer transforms, GpuBuffer arguments, int argumentsOffset, int lodLevel, RenderAttributes attributes );

	//
	// Copies, grabs and mips
	//

	/// <summary>Copy one mip and slice of a texture into another of the same size and format.</summary>
	public abstract void CopyTexture( Texture source, Texture destination, int sourceMip, int sourceSlice, int destinationMip, int destinationSlice );

	/// <summary>Copy (resolving) what's bound inside <paramref name="viewport"/> into a texture (<see cref="Graphics.GrabFrameTexture(string, RenderAttributes, Graphics.DownsampleMethod, int)"/>).</summary>
	public abstract void ResolveColor( Texture destination, Rect viewport );
	public abstract void ResolveDepth( Texture destination, Rect viewport );

	/// <summary>Fill a texture's mips after <paramref name="initialMip"/> from it (<see cref="Graphics.GenerateMipMaps"/>).</summary>
	public abstract void GenerateMipMaps( Texture texture, Graphics.DownsampleMethod method, int initialMip );

	//
	// Compute
	//

	/// <summary>Run a compute shader over a number of threads, or over the groups an indirect buffer holds at a byte offset.</summary>
	public abstract void Dispatch( Material computeMaterial, RenderAttributes attributes, int threadsX, int threadsY, int threadsZ );
	public abstract void DispatchIndirect( Material computeMaterial, RenderAttributes attributes, GpuBuffer indirectBuffer, uint bufferOffset );

	//
	// Barriers
	//

	/// <summary>Move a texture (or one mip, -1 for all) to a state.</summary>
	public abstract void ResourceBarrierTransition( Texture texture, ResourceState state, int mip );

	/// <summary>Move a buffer to a state, from an unknown one or from a known one.</summary>
	public abstract void ResourceBarrierTransition( GpuBuffer buffer, ResourceState state );
	public abstract void ResourceBarrierTransition( GpuBuffer buffer, ResourceState before, ResourceState after );

	/// <summary>Make shader writes visible to later shader reads and writes, keeping the layout.</summary>
	public abstract void UavBarrier( Texture texture );
	public abstract void UavBarrier( GpuBuffer buffer );

	//
	// Buffers
	//

	/// <summary>Write bytes into a buffer at a byte offset.</summary>
	public abstract void SetBufferData( GpuBuffer buffer, IntPtr data, int size, int offset );

	/// <summary>Read bytes from a buffer once the GPU has written them.</summary>
	public abstract void ReadBufferAsync( GpuBuffer buffer, AsyncGPUReadback.BufferReadDelegate callback, int offset, int size );

	/// <summary>Copy bytes from one buffer to another.</summary>
	public abstract void CopyBuffer( GpuBuffer source, GpuBuffer destination, uint sourceOffset, uint destinationOffset, uint size );

	/// <summary>Fill a buffer with one 32-bit value.</summary>
	public abstract void FillBuffer( GpuBuffer buffer, uint value );

	/// <summary>An append or counter buffer's hidden count: copied into another buffer, or set.</summary>
	public abstract void CopyStructureCount( GpuBuffer source, GpuBuffer destination, uint destinationOffset );
	public abstract void SetStructureCount( GpuBuffer buffer, uint value );
}
