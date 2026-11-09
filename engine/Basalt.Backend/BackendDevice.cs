using Basalt.Gpu;
using System.Diagnostics;
using System.Threading;

namespace Basalt.Backend;

/// <summary>
/// Basalt's <see cref="GpuDevice"/> on <see cref="Basalt.Gpu.Device"/>: resources map one-to-one onto Basalt.Gpu's, and
/// materials onto the backend's built-in programs. What only Source 2 has (asset loading, skinning, the sky, engine
/// compute shaders) is stood in for: placeholder textures, materials that draw nothing. See docs/managed/basalt-gpu.md.
/// </summary>
public sealed class BackendDevice : GpuDevice, IDisposable
{
	readonly Stopwatch clock = Stopwatch.StartNew();
	readonly Dictionary<string, GpuTexture> loadedTextures = new( StringComparer.OrdinalIgnoreCase );
	readonly Dictionary<string, GpuMaterial> sharedMaterials = new( StringComparer.OrdinalIgnoreCase );
	readonly Dictionary<IntPtr, SyncPoint> semaphores = new();
	readonly Dictionary<IntPtr, BufferHandle> constantBuffers = new();
	readonly List<(SyncPoint Point, BufferHandle Buffer, int Bytes, GpuReadback Callback)> readbacks = new();
	long nextHandle;
	int vertexCache;

	/// <summary>The Basalt.Gpu device it renders with.</summary>
	public Device Gpu { get; }

	internal MeshProgram Mesh { get; }

	/// <summary>The bindless index of the comparison sampler shadow maps are read with.</summary>
	internal int ShadowSampler { get; }

	/// <summary>A zeroed storage buffer, bound where a frame hasn't provided one.</summary>
	internal BufferHandle EmptyStorage { get; }

	readonly SamplerHandle shadowSampler;
	readonly BackendTexture black;
	readonly BackendTexture sheetMetadata;

	/// <summary>
	/// Render Basalt on <paramref name="gpu"/>, which stays the caller's.
	/// </summary>
	public BackendDevice( Device gpu )
	{
		Gpu = gpu;
		Mesh = new MeshProgram( gpu );

		shadowSampler = gpu.CreateSampler( new SamplerInfo
		{
			MinFilter = Filter.Linear,
			MagFilter = Filter.Linear,
			AddressU = AddressMode.Clamp,
			AddressV = AddressMode.Clamp,
			AddressW = AddressMode.Clamp,
			Compare = CompareFunction.GreaterEqual,
			MaxAnisotropy = 1,
		} );
		ShadowSampler = gpu.SamplerIndex( shadowSampler );

		EmptyStorage = gpu.CreateBuffer( new BufferInfo { Size = 256, Usage = BufferUsage.Storage, Name = "Basalt empty" } );
		gpu.WriteBuffer( EmptyStorage, new byte[256] );

		black = Placeholder( "Black", cube: false );
		sheetMetadata = Placeholder( "Sheet metadata", cube: false );
	}

	BackendTexture Placeholder( string name, bool cube )
	{
		var texture = (BackendTexture)CreateTexture( new TextureDesc { Width = 1, Height = 1, Depth = 1, Format = TextureFormat.RGBA8888, Samples = 1, Mips = 1, Cube = cube, RenderTarget = cube }, name );
		Clear( texture, Color.Black );
		return texture;
	}

	static Format Map( TextureFormat format ) => format switch
	{
		TextureFormat.RGBA8888 => Format.RGBA8Unorm,
		TextureFormat.BGRA8888 => Format.BGRA8Unorm,
		TextureFormat.A8 => Format.R8Unorm,
		TextureFormat.R16F => Format.R16Float,
		TextureFormat.R32F => Format.R32Float,
		TextureFormat.R32_UINT => Format.R32Uint,
		TextureFormat.RG1616F => Format.RG16Float,
		TextureFormat.RG3232F => Format.RG32Float,
		TextureFormat.R11G11B10F => Format.RG11B10Float,
		TextureFormat.RGB323232F => Format.RGB32Float,
		TextureFormat.RGBA16161616 => Format.RGBA16Unorm,
		TextureFormat.RGBA16161616F => Format.RGBA16Float,
		TextureFormat.RGBA32323232F => Format.RGBA32Float,
		TextureFormat.RGBA1010102 => Format.RGB10A2Unorm,
		TextureFormat.BGRA1010102 => Format.BGR10A2Unorm,
		TextureFormat.D16 => Format.D16Unorm,
		TextureFormat.D32 => Format.D32Float,
		TextureFormat.D24S8 => Format.D24UnormS8Uint,
		TextureFormat.D32FS8 => Format.D32FloatS8Uint,
		_ => Format.RGBA8Unorm,
	};

	/// <summary>The format a texture is in on the GPU (D24S8 may be D32S8 there, as <see cref="Device"/> makes it).</summary>
	internal static Format FormatOf( BackendTexture texture ) => Map( texture.Format );

	/// <inheritdoc/>
	public override GpuTexture CreateTexture( in TextureDesc desc, string name )
	{
		var format = Map( desc.Format );
		var samples = Math.Max( desc.Samples, 1 );
		var usage = TextureUsage.CopySource | TextureUsage.CopyDestination;
		if ( samples == 1 ) usage |= TextureUsage.Sampled;
		if ( desc.RenderTarget ) usage |= format.IsDepth() ? TextureUsage.DepthTarget : TextureUsage.ColorTarget;
		if ( desc.Storage ) usage |= TextureUsage.Storage;

		// Formats that can't be drawn to or written by compute here still make a texture; nothing will draw to it
		if ( !Gpu.Supports( format, usage ) ) usage &= ~(TextureUsage.ColorTarget | TextureUsage.Storage);

		var info = new TextureInfo
		{
			Width = desc.Width,
			Height = desc.Height,
			Depth = desc.Depth > 1 ? desc.Depth : 1,
			Layers = 1,
			Mips = Math.Max( desc.Mips, 1 ),
			Samples = samples,
			Dimension = desc.Cube ? TextureDimension.TextureCube : desc.Depth > 1 ? TextureDimension.Texture3D : TextureDimension.Texture2D,
			Format = format,
			Usage = usage,
			Name = name,
		};

		var texture = new BackendTexture( this, Gpu.CreateTexture( info ), desc, name );
		if ( !desc.Data.IsEmpty && !desc.Cube && desc.Depth <= 1 )
		{
			Gpu.WriteTexture( texture.Handle, desc.Data.Span );
			texture.Layout = ImageLayout.ShaderRead;
		}

		return texture;
	}

	/// <summary>
	/// Assets aren't loaded here: every path gets a black 1x1 stand-in (a cube for cube paths), so frames that bind
	/// lookup tables still bind something.
	/// </summary>
	public override GpuTexture LoadTexture( string path )
	{
		lock ( loadedTextures )
		{
			if ( loadedTextures.TryGetValue( path, out var texture ) ) return texture;
			return loadedTextures[path] = Placeholder( path, cube: path.Contains( "cube", StringComparison.OrdinalIgnoreCase ) );
		}
	}

	/// <inheritdoc/>
	public override GpuTexture Black => black;

	/// <inheritdoc/>
	public override int MissingTextureViewIndex( bool srgb ) => black.ViewIndex( srgb, TextureViewDimension.Default );

	/// <inheritdoc/>
	public override void Clear( GpuTexture texture, Color color )
	{
		var t = (BackendTexture)texture;
		var list = Gpu.BeginCommandList( QueueType.Graphics, "Clear texture" );
		list.Barrier( TextureBarrier.Transition( t.Handle, t.Layout, ImageLayout.CopyDestination, PipelineStage.AllCommands, MemoryAccess.MemoryWrite, PipelineStage.Transfer, MemoryAccess.TransferWrite ) );
		list.ClearTexture( t.Handle, new System.Numerics.Vector4( color.r, color.g, color.b, color.a ) );
		list.Barrier( TextureBarrier.Transition( t.Handle, ImageLayout.CopyDestination, ImageLayout.ShaderRead, PipelineStage.Transfer, MemoryAccess.TransferWrite, PipelineStage.AllCommands, MemoryAccess.MemoryRead ) );
		Gpu.Wait( Gpu.Submit( list ) );
		t.Layout = ImageLayout.ShaderRead;
	}

	/// <inheritdoc/>
	public override IDisposable CreateBuffer( int elementCount, int elementSize, GpuBuffer.UsageFlags usage, string name )
	{
		var bufferUsage = BufferUsage.Storage;
		if ( (usage & GpuBuffer.UsageFlags.Vertex) != 0 ) bufferUsage |= BufferUsage.Vertex;
		if ( (usage & GpuBuffer.UsageFlags.Index) != 0 ) bufferUsage |= BufferUsage.Index;
		if ( (usage & GpuBuffer.UsageFlags.IndirectDrawArguments) != 0 ) bufferUsage |= BufferUsage.Indirect;

		var size = (long)elementCount * elementSize;
		return new BackendBuffer( this, Gpu.CreateBuffer( new BufferInfo { Size = size, Usage = bufferUsage, Name = name } ), size, name );
	}

	/// <inheritdoc/>
	public override void SetData( GpuBuffer buffer, ReadOnlySpan<byte> data ) => Gpu.WriteBuffer( ((BackendBuffer)buffer.Resource).Handle, data );

	/// <inheritdoc/>
	public override ShaderParameters CreateParameters() => new BackendParameters();

	/// <inheritdoc/>
	public override GpuTexture SheetMetadata => sheetMetadata;

	/// <summary>
	/// The depth-only material draws with the mesh program; other asset materials don't exist here.
	/// </summary>
	public override GpuMaterial LoadMaterial( string path ) => Shared( path );

	/// <inheritdoc/>
	public override GpuMaterial MaterialFromShader( string shaderPath ) => Shared( shaderPath );

	GpuMaterial Shared( string path )
	{
		lock ( sharedMaterials )
		{
			if ( sharedMaterials.TryGetValue( path, out var material ) ) return material;
			return sharedMaterials[path] = new BackendMaterial( path, ProgramFor( path ), Color.White );
		}
	}

	static BackendProgram ProgramFor( string shaderPath )
	{
		if ( string.Equals( shaderPath, Features.MeshRenderFeature.DefaultShader, StringComparison.OrdinalIgnoreCase ) ) return BackendProgram.Mesh;
		if ( string.Equals( shaderPath, Features.MeshRenderFeature.DepthOnlyMaterial, StringComparison.OrdinalIgnoreCase ) ) return BackendProgram.Mesh;
		return BackendProgram.None;
	}

	/// <inheritdoc/>
	public override GpuMaterial CreateMaterial( string name, string shaderPath ) => new BackendMaterial( name, ProgramFor( shaderPath ), Color.White );

	/// <summary>
	/// A lit material of one colour, for meshes made with <see cref="BackendMesh.Create"/>.
	/// </summary>
	public GpuMaterial CreateMaterial( string name, Color albedo ) => new BackendMaterial( name, BackendProgram.Mesh, albedo );

	internal override GpuContext CreateContext( GpuContext frame = null ) => new BackendContext( this, (BackendContext)frame );

	/// <summary>Async compute isn't wired yet: contexts all record on the graphics queue.</summary>
	public override bool SupportsAsyncCompute => false;

	/// <inheritdoc/>
	public override bool SupportsDrawIndirectCount => Gpu.Capabilities.DrawIndirectCount;

	/// <inheritdoc/>
	public override int AllocateVertexCache( int vertices ) => Interlocked.Add( ref vertexCache, vertices ) - vertices;

	/// <inheritdoc/>
	public override Vector4 MorphSubrect( int handle ) => default;

	/// <inheritdoc/>
	public override void BeginSubmitting( GpuSwapChain swapChain ) { }

	/// <inheritdoc/>
	public override void SetSubmitTag( ulong tag ) { }

	/// <inheritdoc/>
	public override float RenderTime => (float)clock.Elapsed.TotalSeconds;

	/// <inheritdoc/>
	public override IntPtr CreateConstantBuffer( int size )
	{
		var handle = (IntPtr)Interlocked.Increment( ref nextHandle );
		var buffer = Gpu.CreateBuffer( new BufferInfo { Size = size, Usage = BufferUsage.Uniform, Name = "Basalt constants" } );
		lock ( constantBuffers ) constantBuffers[handle] = buffer;
		return handle;
	}

	/// <inheritdoc/>
	public override void DestroyConstantBuffer( IntPtr buffer )
	{
		lock ( constantBuffers )
		{
			if ( constantBuffers.Remove( buffer, out var handle ) ) Gpu.Destroy( handle );
		}
	}

	/// <inheritdoc/>
	public override int SwapChainSamples( GpuSwapChain swapChain ) => 1;

	//
	// Semaphores: Basalt's binary semaphores become points on Basalt.Gpu's queue timelines
	//

	internal GpuSemaphore NewSemaphore() => new( (IntPtr)Interlocked.Increment( ref nextHandle ) );

	internal void Signalled( GpuSemaphore semaphore, SyncPoint point )
	{
		lock ( semaphores ) semaphores[semaphore.Handle] = point;
	}

	internal SyncPoint PointOf( GpuSemaphore semaphore )
	{
		lock ( semaphores )
		{
			if ( !semaphores.Remove( semaphore.Handle, out var point ) ) throw new InvalidOperationException( $"Waiting on semaphore {semaphore.Handle} before its signaller was submitted" );
			return point;
		}
	}

	//
	// Readbacks: copies to the CPU, delivered once their submission has finished
	//

	internal void AddReadback( SyncPoint point, BufferHandle buffer, int bytes, GpuReadback callback )
	{
		lock ( readbacks ) readbacks.Add( (point, buffer, bytes, callback) );
	}

	/// <summary>
	/// Deliver finished readbacks. Call once a frame; <see cref="BeginFrame"/> does.
	/// </summary>
	public void Poll()
	{
		List<(SyncPoint Point, BufferHandle Buffer, int Bytes, GpuReadback Callback)> due = null;
		lock ( readbacks )
		{
			for ( int i = 0; i < readbacks.Count; i++ )
			{
				if ( !Gpu.IsComplete( readbacks[i].Point ) ) continue;
				(due ??= new()).Add( readbacks[i] );
				readbacks.RemoveAt( i-- );
			}
		}

		if ( due is null ) return;
		foreach ( var (_, buffer, bytes, callback) in due )
		{
			callback( Gpu.Map( buffer )[..bytes] );
			Gpu.Destroy( buffer );
		}
	}

	/// <summary>
	/// Start the next frame: <see cref="Device.BeginFrame"/>, then deliver finished readbacks.
	/// </summary>
	public void BeginFrame()
	{
		Gpu.BeginFrame();
		Poll();
	}

	/// <summary>
	/// The Basalt.Gpu texture behind a Basalt texture made here, and the layout its last recorded use left it in.
	/// </summary>
	public static (TextureHandle Texture, ImageLayout Layout) Unwrap( GpuTexture texture ) => texture is BackendTexture t ? (t.Handle, t.Layout) : default;

	/// <summary>
	/// Note that recording outside Basalt moved a texture to <paramref name="layout"/>.
	/// </summary>
	public static void SetLayout( GpuTexture texture, ImageLayout layout )
	{
		if ( texture is BackendTexture t ) t.Layout = layout;
	}

	/// <summary>
	/// Free what this backend made. Basalt's own resources (targets, buffers) are freed by Basalt or with the device.
	/// </summary>
	public void Dispose()
	{
		Gpu.WaitIdle();
		Poll();
		Mesh.Dispose();
		Gpu.Destroy( shadowSampler );
		Gpu.Destroy( EmptyStorage );
	}
}

/// <summary>
/// A <see cref="RenderHost"/> rendering through a <see cref="BackendDevice"/>, with the passes it can't run yet off:
/// occlusion culling, contact shadows and async compute, which need engine compute shaders.
/// </summary>
public class BackendRenderHost( BackendDevice device ) : RenderHost
{
	/// <inheritdoc/>
	public override GpuDevice Device => device;

	/// <inheritdoc/>
	public override bool AsyncCompute => false;

	/// <inheritdoc/>
	public override bool ContactShadows => false;

	/// <inheritdoc/>
	public override bool OcclusionCulling => false;
}
