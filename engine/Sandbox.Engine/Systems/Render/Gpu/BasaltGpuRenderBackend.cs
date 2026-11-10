using Basalt.Backend;
using Basalt.Gpu;
using Basalt.Source2;
using Basalt.Source2.Shaders;
using Sandbox.Rendering;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using GpuCommandList = Basalt.Gpu.CommandList;
using GpuDevice = Basalt.Gpu.Device;
using GpuFormat = Basalt.Gpu.Format;
using Num = System.Numerics;
using ProgramType = Basalt.Source2.Shaders.ShaderProgramType;
using RenderViewport = NativeEngine.RenderViewport;

namespace Sandbox;

/// <summary>
/// <see cref="Graphics"/> on a Basalt.Gpu device of its own: render blocks with no view (<see cref="Graphics.Scope.CreateOn"/>)
/// record into one of the device's command lists through Basalt.Backend's <see cref="GraphicsContext"/>, which tracks
/// layouts and opens rendering scopes itself, and draw with the engine's compiled shaders through
/// <see cref="ShaderDrawer"/>, bound by attribute name from managed attributes. What <c>Painter.Begin( Texture )</c>
/// needs runs here: CPU-vertex and instanced draws, clears, render targets, frame grabs, gaussian mips and compute.
/// The rest throws <see cref="NotSupportedException"/> (docs/managed/basalt-gpu-path.md).
/// </summary>
/// <remarks>
/// Engine resources get a backing on this device the first time it uses them. Render targets are made empty with the
/// texture's size, format and mips; other textures are copied from Source 2's pixels (mip 0); buffers made while
/// recording for this device start empty, and others start with what Source 2's copy holds. A backing lives until its
/// resource is disposed, or until this backend is.
/// </remarks>
internal sealed class BasaltGpuRenderBackend : RenderBackend, IDisposable
{
	readonly GpuDevice gpu;
	readonly ShaderLibrary library;
	readonly ShaderDefaults defaults;
	readonly ShaderDrawer drawer;
	readonly GraphicsContext context;
	readonly bool ownsDevice;
	readonly IntPtr uploadKey;
	static int nextKey;

	readonly ConditionalWeakTable<Texture, Backing<TextureHandle>> textures = new();
	readonly ConditionalWeakTable<GpuBuffer, Backing<BufferHandle>> buffers = new();
	readonly Dictionary<TextureHandle, Texture> textureOwners = new();
	readonly HashSet<BufferHandle> ownedBuffers = new();
	readonly Dictionary<SamplerState, (SamplerHandle Sampler, int Index)> samplers = new();
	readonly Dictionary<string, ShaderFile> shaders = new( StringComparer.OrdinalIgnoreCase );
	readonly Dictionary<uint, string> names = new();
	readonly ShaderAttributes lowered = new();

	GpuCommandList list;

	// SetRenderState's material, for the draw after it
	ShaderFile pendingShader;
	RenderAttributes pendingAttributes;

	sealed class Backing<T>( T handle )
	{
		public T Handle = handle;
	}

	/// <summary>
	/// A backend on a device of its own, introduced to the driver with Source 2's application and engine names so it gets
	/// the same driver profile (AMD's changes trilinear filtering otherwise). <paramref name="source2Names"/> false uses
	/// Basalt's, to show the difference.
	/// </summary>
	public static BasaltGpuRenderBackend Create( bool validation = false, Action<DeviceMessage> message = null, bool source2Names = true )
	{
		var gpu = GpuDevice.Create( new DeviceOptions
		{
			ApplicationName = source2Names ? "Sbox" : "Basalt",
			EngineName = source2Names ? "Source2" : "Basalt",
			Validation = validation,
			Presentation = false,
			Message = message,
		} );

		return new BasaltGpuRenderBackend( gpu, ownsDevice: true );
	}

	BasaltGpuRenderBackend( GpuDevice gpu, bool ownsDevice )
	{
		this.gpu = gpu;
		this.ownsDevice = ownsDevice;
		library = new ShaderLibrary( gpu, ContentFiles.FindGame() );
		defaults = new ShaderDefaults( gpu );
		drawer = new ShaderDrawer( gpu, library, defaults );
		context = new GraphicsContext( gpu, drawer, null );
		uploadKey = new IntPtr( -1 - System.Threading.Interlocked.Increment( ref nextKey ) );
		Register( this );
	}

	/// <summary>The device everything records on.</summary>
	public GpuDevice Device => gpu;

	/// <summary>The context blocks record through.</summary>
	public GraphicsContext Context => context;

	//
	// Blocks and resources
	//

	public override RenderAttributes BeginBlock()
	{
		if ( list is not null ) throw new InvalidOperationException( "A Basalt.Gpu block is already recording." );

		list = gpu.BeginCommandList( QueueType.Graphics, "Graphics block" );
		context.Begin( list );
		pendingShader = null;
		pendingAttributes = null;
		return RenderAttributes.CreateManaged();
	}

	public override void EndBlock()
	{
		try
		{
			context.FinishReadable();
			gpu.Submit( list );
		}
		finally
		{
			list = null;
			pendingAttributes = null;
		}
	}

	/// <summary>Wait until everything submitted has finished.</summary>
	public void WaitIdle() => gpu.WaitIdle();

	public override IntPtr UploadContext => uploadKey;

	public override int TextureIndex( Texture texture ) => gpu.BindlessIndex( TextureOf( texture ) );

	public override int SamplerIndex( SamplerState state )
	{
		if ( samplers.TryGetValue( state, out var made ) ) return made.Index;

		var info = state.Filter switch
		{
			FilterMode.Point => SamplerInfo.Point(),
			FilterMode.Bilinear => SamplerInfo.Linear() with { MipFilter = Filter.Nearest },
			FilterMode.Anisotropic => SamplerInfo.Linear( anisotropy: state.MaxAnisotropy ),
			_ => SamplerInfo.Linear(),
		};
		info.AddressU = Address( state.AddressModeU );
		info.AddressV = Address( state.AddressModeV );
		info.AddressW = Address( state.AddressModeW );
		info.MaxLod = 15;

		var sampler = gpu.CreateSampler( info );
		made = (sampler, gpu.SamplerIndex( sampler ));
		samplers[state] = made;
		return made.Index;
	}

	static AddressMode Address( TextureAddressMode mode ) => mode switch
	{
		TextureAddressMode.Mirror or TextureAddressMode.MirrorOnce => AddressMode.Mirror,
		TextureAddressMode.Clamp => AddressMode.Clamp,
		TextureAddressMode.Border => AddressMode.Border,
		_ => AddressMode.Wrap,
	};

	public override void Release( object resource )
	{
		switch ( resource )
		{
			case Texture texture when textures.TryGetValue( texture, out var backing ):
				textures.Remove( texture );
				textureOwners.Remove( backing.Handle );
				context.Forget( backing.Handle );
				gpu.Destroy( backing.Handle );
				break;

			case GpuBuffer buffer when buffers.TryGetValue( buffer, out var backing ):
				buffers.Remove( buffer );
				ownedBuffers.Remove( backing.Handle );
				gpu.Destroy( backing.Handle );
				break;
		}
	}

	/// <summary>
	/// A texture's backing: a target made to match a render target, or a copy of mip 0 of anything else, as RGBA8.
	/// </summary>
	internal TextureHandle TextureOf( Texture texture )
	{
		ArgumentNullException.ThrowIfNull( texture );
		if ( textures.TryGetValue( texture, out var backing ) ) return backing.Handle;

		TextureHandle handle;
		if ( texture.IsRenderTarget )
		{
			var info = TextureInfo.RenderTarget( texture.Width, texture.Height, FormatOf( texture.ImageFormat ), name: $"Engine target {texture.Width}x{texture.Height}" );
			info.Mips = Math.Max( texture.Mips, 1 );
			info.Usage |= TextureUsage.Storage;
			handle = gpu.CreateTexture( info );
			context.Track( handle, ImageLayout.Undefined );
		}
		else
		{
			var pixels = texture.GetPixels();
			handle = gpu.CreateTexture( TextureInfo.Texture2D( texture.Width, texture.Height, GpuFormat.RGBA8Unorm, name: $"Engine image {texture.Width}x{texture.Height}" ) );
			gpu.WriteTexture( handle, MemoryMarshal.AsBytes( pixels.AsSpan() ) );
			context.Track( handle, ImageLayout.ShaderRead );
		}

		textures.Add( texture, new Backing<TextureHandle>( handle ) );
		textureOwners[handle] = texture;
		return handle;
	}

	static GpuFormat FormatOf( ImageFormat format ) => format switch
	{
		ImageFormat.RGBA8888 => GpuFormat.RGBA8Unorm,
		ImageFormat.BGRA8888 => GpuFormat.BGRA8Unorm,
		ImageFormat.RGBA16161616F => GpuFormat.RGBA16Float,
		_ => throw new NotSupportedException( $"Render target format {format} on Basalt.Gpu" ),
	};

	/// <summary>
	/// A buffer's backing. One made while recording for another device starts with what Source 2's copy holds.
	/// </summary>
	internal BufferHandle BufferOf( GpuBuffer buffer )
	{
		ArgumentNullException.ThrowIfNull( buffer );
		if ( buffers.TryGetValue( buffer, out var backing ) ) return backing.Handle;

		var size = Math.Max( (long)buffer.ElementCount * buffer.ElementSize, 16 );
		var handle = gpu.CreateBuffer( new BufferInfo
		{
			Size = size,
			Usage = BufferUsage.Storage | BufferUsage.Index | BufferUsage.Vertex | BufferUsage.Indirect | BufferUsage.CopySource | BufferUsage.CopyDestination,
			Name = "Engine buffer",
		} );

		buffers.Add( buffer, new Backing<BufferHandle>( handle ) );
		ownedBuffers.Add( handle );

		if ( buffer.Origin != this )
		{
			var bytes = new byte[buffer.ElementCount * buffer.ElementSize];
			buffer.GetData<byte>( bytes );
			gpu.WriteBuffer( handle, bytes );
		}

		return handle;
	}

	/// <summary>The compiled shader a material draws with (its shader, without material values).</summary>
	ShaderFile ShaderOf( Material material )
	{
		var path = material.native.GetString( "shader", "" );
		if ( string.IsNullOrEmpty( path ) ) return null;
		if ( shaders.TryGetValue( path, out var shader ) ) return shader;

		var file = path.Replace( '\\', '/' );
		if ( file.EndsWith( ".vfx", StringComparison.OrdinalIgnoreCase ) ) file = file[..^4] + ".shader";
		if ( !file.EndsWith( ".shader", StringComparison.OrdinalIgnoreCase ) ) file += ".shader";
		if ( !file.Contains( '/' ) ) file = "shaders/" + file;

		shader = library.Load( file );
		shaders[path] = shader;
		if ( shader is not null ) AddNames( shader );
		return shader;
	}

	ShaderFile Shader( string path ) => shaders.TryGetValue( path, out var shader ) ? shader : shaders[path] = AddNames( library.Load( path ) );

	/// <summary>Learn the attribute and combo names a shader reads, so tokens can be turned back into them.</summary>
	ShaderFile AddNames( ShaderFile shader )
	{
		foreach ( var type in (ProgramType[])[ProgramType.Features, ProgramType.Vertex, ProgramType.Pixel, ProgramType.Compute] )
		{
			if ( shader[type] is not { } program ) continue;
			foreach ( var variable in program.Variables )
			{
				if ( !string.IsNullOrEmpty( variable.Name ) ) names.TryAdd( new StringToken( variable.Name ).Value, variable.Name );
				if ( !string.IsNullOrEmpty( variable.Attribute ) ) names.TryAdd( new StringToken( variable.Attribute ).Value, variable.Attribute );
			}

			foreach ( var combo in program.DynamicCombos ) names.TryAdd( new StringToken( combo.Name ).Value, combo.Name );
		}

		return shader;
	}

	string Name( uint token ) => names.TryGetValue( token, out var name ) ? name : StringToken.GetValue( token );

	/// <summary>Lower managed attributes into what <see cref="ShaderDrawer"/> binds by name.</summary>
	ShaderAttributes Lower( RenderAttributes attributes )
	{
		lowered.Clear();
		if ( attributes?.Managed is not { } managed )
			throw new NotSupportedException( "Native render attributes can't be drawn with on Basalt.Gpu" );

		foreach ( var (token, value) in managed.Values )
		{
			if ( Name( token ) is not { } name ) continue;
			switch ( value )
			{
				case float[] floats:
					lowered.Set( name, floats );
					break;
				case ManagedRenderAttributes.TextureValue { Texture: { } texture, Mip: < 0 }:
					lowered.Set( name, TextureOf( texture ) );
					break;
				case ManagedRenderAttributes.TextureValue t:
					throw new NotSupportedException( $"{name}: a single mip of a texture, on Basalt.Gpu" );
				case GpuBuffer buffer:
					lowered.Set( name, BufferOf( buffer ) );
					break;
				case byte[] data:
					lowered.SetData( name, data );
					break;
			}
		}

		foreach ( var (token, value) in managed.Combos )
		{
			if ( Name( token ) is { } name ) lowered.SetCombo( name, value );
		}

		return lowered;
	}

	ShaderFile Require( Material material ) => ShaderOf( material ) ?? throw new NotSupportedException( $"{material} has no compiled shader Basalt.Gpu can load" );

	GpuCommandList Recording => list ?? throw new InvalidOperationException( "Basalt.Gpu draws need a render block (Graphics.Scope.CreateOn)." );

	//
	// Targets and viewport
	//

	public override RenderViewport GetViewport()
	{
		var v = context.Viewport;
		return new RenderViewport( new Rect( v.X, v.Y, v.Z, v.W ), 0, 1 );
	}

	public override void SetViewport( in RenderViewport viewport ) => SetViewport( viewport.Rect );

	public override void SetViewport( Rect rect ) => context.Viewport = new Num.Vector4( rect.Left, rect.Top, rect.Width, rect.Height );

	public override void BindRenderTarget( RenderTarget target )
	{
		_ = Recording;
		context.SetTarget( TextureOf( target.ColorTarget ) );
	}

	public override void Clear( Color color, bool clearColor, bool clearDepth, bool clearStencil )
	{
		_ = Recording;
		if ( clearColor ) context.Clear( new Num.Vector4( color.r, color.g, color.b, color.a ) );
	}

	public override void ClearTexture( Texture texture, Color color )
	{
		_ = Recording;
		context.ClearTexture( TextureOf( texture ), new Num.Vector4( color.r, color.g, color.b, color.a ) );
	}

	//
	// Draws
	//

	static PrimitiveTopology Topology( Graphics.PrimitiveType type ) => type switch
	{
		Graphics.PrimitiveType.Points => PrimitiveTopology.PointList,
		Graphics.PrimitiveType.Lines => PrimitiveTopology.LineList,
		Graphics.PrimitiveType.LineStrip => PrimitiveTopology.LineStrip,
		Graphics.PrimitiveType.Triangles => PrimitiveTopology.TriangleList,
		Graphics.PrimitiveType.TriangleStrip => PrimitiveTopology.TriangleStrip,
		_ => throw new NotSupportedException( $"{type} on Basalt.Gpu" ),
	};

	public override unsafe void DrawVertices( Type vertexType, int vertexSize, IntPtr vertices, int vertexCount, IntPtr indices, int indexCount, Material material, RenderAttributes attributes, Graphics.PrimitiveType primitiveType )
	{
		_ = Recording;
		if ( vertexType != typeof( Vertex ) ) throw new NotSupportedException( $"Vertices of {vertexType} on Basalt.Gpu" );
		if ( indices != IntPtr.Zero ) throw new NotSupportedException( "Indexed CPU vertices on Basalt.Gpu" );

		var shader = Require( material );
		var bytes = new ReadOnlySpan<byte>( (void*)vertices, vertexSize * vertexCount );
		context.DrawVertices( shader, Lower( attributes ), bytes, VertexStreamLayout.EngineVertex, vertexCount, Topology( primitiveType ) );
	}

	public override bool SetRenderState( Material material, RenderAttributes attributes, Type vertexType )
	{
		_ = Recording;
		if ( vertexType is not null ) throw new NotSupportedException( $"Vertex buffers of {vertexType} on Basalt.Gpu" );

		pendingShader = Require( material );
		pendingAttributes = attributes;
		return true;
	}

	public override void DrawIndexedInstanced( GpuBuffer indexBuffer, Graphics.PrimitiveType primitiveType, int instanceCount )
	{
		if ( primitiveType != Graphics.PrimitiveType.Triangles ) throw new NotSupportedException( $"Instanced {primitiveType} on Basalt.Gpu" );
		if ( indexBuffer.ElementSize != sizeof( uint ) ) throw new NotSupportedException( "16-bit index buffers on Basalt.Gpu" );

		context.DrawIndexedInstanced( pendingShader, Lower( pendingAttributes ), BufferOf( indexBuffer ), indexBuffer.ElementCount, instanceCount );
	}

	public override void Draw( GpuBuffer vertexBuffer, Graphics.PrimitiveType primitiveType, int startVertex, int vertexCount ) => throw Unsupported( "Vertex buffer draws" );
	public override void DrawIndexed( GpuBuffer vertexBuffer, GpuBuffer indexBuffer, Graphics.PrimitiveType primitiveType, int startIndex, int indexCount ) => throw Unsupported( "Vertex buffer draws" );
	public override void DrawInstancedIndirect( GpuBuffer vertexBuffer, Graphics.PrimitiveType primitiveType, GpuBuffer indirectBuffer, uint bufferOffset, uint drawCount, uint stride ) => throw Unsupported( "Indirect draws" );
	public override void DrawIndexedInstancedIndirect( GpuBuffer vertexBuffer, GpuBuffer indexBuffer, Graphics.PrimitiveType primitiveType, GpuBuffer indirectBuffer, uint bufferOffset, uint drawCount, uint stride ) => throw Unsupported( "Indirect draws" );

	//
	// Models
	//

	public override void DrawModel( Model model, ReadOnlySpan<Transform> transforms, int? lodLevel, RenderAttributes attributes ) => throw Unsupported( "Model draws" );
	public override void DrawModel( Model model, int count, RenderAttributes attributes ) => throw Unsupported( "Model draws" );
	public override void DrawModelIndirect( Model model, GpuBuffer arguments, int argumentsOffset, RenderAttributes attributes ) => throw Unsupported( "Model draws" );
	public override void DrawModelIndirect( Model model, GpuBuffer transforms, GpuBuffer arguments, int argumentsOffset, int lodLevel, RenderAttributes attributes ) => throw Unsupported( "Model draws" );

	//
	// Copies, grabs and mips
	//

	public override void CopyTexture( Texture source, Texture destination, int sourceMip, int sourceSlice, int destinationMip, int destinationSlice ) => throw Unsupported( "Texture copies" );

	public override void ResolveColor( Texture destination, Rect viewport )
	{
		_ = Recording;
		var saved = context.Viewport;
		SetViewport( viewport );
		context.CopyTarget( TextureOf( destination ) );
		context.Viewport = saved;
	}

	public override void ResolveDepth( Texture destination, Rect viewport ) => throw Unsupported( "Depth" );

	public override void GenerateMipMaps( Texture texture, Graphics.DownsampleMethod method, int initialMip )
	{
		_ = Recording;
		if ( method is not (Graphics.DownsampleMethod.GaussianBlur or Graphics.DownsampleMethod.GaussianBorder) || initialMip != 0 )
			throw Unsupported( $"{method} mips from mip {initialMip}" );

		context.GenerateMips( TextureOf( texture ), Shader( "shaders/downsample_cs.shader" ), (int)method );
	}

	//
	// Compute
	//

	public override void Dispatch( Material computeMaterial, RenderAttributes attributes, int threadsX, int threadsY, int threadsZ )
	{
		_ = Recording;
		if ( !context.Dispatch( Require( computeMaterial ), Lower( attributes ), threadsX, threadsY, threadsZ ) )
			throw new InvalidOperationException( $"{computeMaterial} didn't dispatch on Basalt.Gpu" );
	}

	public override void DispatchIndirect( Material computeMaterial, RenderAttributes attributes, GpuBuffer indirectBuffer, uint bufferOffset ) => throw Unsupported( "Indirect dispatches" );

	//
	// Barriers: layouts are tracked, so a transition only orders memory
	//

	public override void ResourceBarrierTransition( Texture texture, ResourceState state, int mip ) => Barrier();
	public override void ResourceBarrierTransition( GpuBuffer buffer, ResourceState state ) => Barrier();
	public override void ResourceBarrierTransition( GpuBuffer buffer, ResourceState before, ResourceState after ) => Barrier();
	public override void UavBarrier( Texture texture ) => Barrier();
	public override void UavBarrier( GpuBuffer buffer ) => Barrier();

	void Barrier()
	{
		_ = Recording;
		context.MemoryBarrier();
	}

	//
	// Buffers
	//

	public override unsafe void SetBufferData( GpuBuffer buffer, IntPtr data, int size, int offset )
	{
		var handle = BufferOf( buffer );
		var bytes = new ReadOnlySpan<byte>( (void*)data, size );

		// In a block, in order with its draws; outside one, now
		if ( list is not null ) context.UpdateBuffer( handle, bytes, offset );
		else gpu.WriteBuffer( handle, bytes, offset );
	}

	public override void ReadBufferAsync( GpuBuffer buffer, AsyncGPUReadback.BufferReadDelegate callback, int offset, int size ) => throw Unsupported( "Buffer readback" );
	public override void CopyBuffer( GpuBuffer source, GpuBuffer destination, uint sourceOffset, uint destinationOffset, uint size ) => throw Unsupported( "Buffer copies" );
	public override void FillBuffer( GpuBuffer buffer, uint value ) => throw Unsupported( "Buffer fills" );
	public override void CopyStructureCount( GpuBuffer source, GpuBuffer destination, uint destinationOffset ) => throw Unsupported( "Structure counts" );
	public override void SetStructureCount( GpuBuffer buffer, uint value ) => throw Unsupported( "Structure counts" );

	static NotSupportedException Unsupported( string what ) => new( $"{what} aren't on Basalt.Gpu yet (docs/managed/basalt-gpu-path.md)" );

	//
	// Readback
	//

	/// <summary>
	/// Mip 0 of an RGBA8 texture's backing, after everything submitted so far: what the device drew into it.
	/// </summary>
	public Color32[] ReadPixels( Texture texture )
	{
		var handle = TextureOf( texture );
		var info = gpu.InfoOf( handle );
		if ( info.Format is not (GpuFormat.RGBA8Unorm or GpuFormat.RGBA8Srgb) ) throw new NotSupportedException( $"Reading {info.Format} back" );

		var readback = gpu.CreateBuffer( new BufferInfo { Size = info.Width * info.Height * 4, Memory = MemoryLocation.Readback, Name = "Readback" } );
		try
		{
			var copy = gpu.BeginCommandList( QueueType.Graphics, "Readback" );
			context.Begin( copy );
			context.CopyToBuffer( handle, readback );
			context.Finish();
			gpu.Wait( gpu.Submit( copy ) );

			var bytes = gpu.Map( readback );
			var pixels = new Color32[info.Width * info.Height];
			bytes[..(pixels.Length * 4)].CopyTo( MemoryMarshal.AsBytes( pixels.AsSpan() ) );
			return pixels;
		}
		finally
		{
			gpu.Destroy( readback );
		}
	}

	/// <summary>
	/// Start the device's next frame: what the frames before the last one used is recycled, and destroyed backings freed.
	/// </summary>
	public void NextFrame() => gpu.BeginFrame();

	public void Dispose()
	{
		Unregister( this );
		gpu.WaitIdle();

		foreach ( var handle in textureOwners.Keys )
		{
			context.Forget( handle );
			gpu.Destroy( handle );
		}
		foreach ( var handle in ownedBuffers ) gpu.Destroy( handle );
		foreach ( var (sampler, _) in samplers.Values ) gpu.Destroy( sampler );
		textureOwners.Clear();
		ownedBuffers.Clear();
		samplers.Clear();
		textures.Clear();
		buffers.Clear();

		context.Release();
		library.Dispose();
		defaults.Dispose();
		if ( ownsDevice ) gpu.Dispose();
	}
}
