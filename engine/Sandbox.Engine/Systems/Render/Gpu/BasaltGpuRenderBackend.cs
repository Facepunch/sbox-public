using Basalt.Backend;
using Basalt.Gpu;
using Basalt.Source2;
using Basalt.Source2.Materials;
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
	readonly ContentFiles files;
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
	/// Basalt's, to show the difference. <paramref name="presentation"/> lets it present to windows; with
	/// <paramref name="frames"/> the first block of each engine frame starts the device's next frame, as the process's
	/// device does.
	/// </summary>
	public static BasaltGpuRenderBackend Create( bool validation = false, Action<DeviceMessage> message = null, bool source2Names = true, bool presentation = false, bool frames = false )
	{
		var gpu = GpuDevice.Create( new DeviceOptions
		{
			ApplicationName = source2Names ? "Sbox" : "Basalt",
			EngineName = source2Names ? "Source2" : "Basalt",
			Validation = validation,
			Presentation = presentation,
			Message = message,
		} );

		return new BasaltGpuRenderBackend( gpu, ownsDevice: true ) { followsFrames = frames };
	}

	/// <summary>A device beside Source 2's, logging its messages, for comparing the two in one process.</summary>
	public static BasaltGpuRenderBackend CreateBeside( bool validation ) => Create( validation, m => Log.Info( $"Basalt.Gpu: {m.Severity} {m.Text}" ) );

	/// <summary>Validation errors the device has reported.</summary>
	public int ValidationErrors => gpu.ValidationErrors;

	bool followsFrames;
	ulong frame;

	/// <summary>Each engine frame is a device frame: starting one waits for the frame before last, then recycles what it used.</summary>
	void FollowFrame()
	{
		if ( !followsFrames || frame == Application.FrameCount ) return;
		frame = Application.FrameCount;
		gpu.BeginFrame();
	}

	BasaltGpuRenderBackend( GpuDevice gpu, bool ownsDevice )
	{
		this.gpu = gpu;
		this.ownsDevice = ownsDevice;
		files = ContentFiles.FindGame();
		library = new ShaderLibrary( gpu, files );
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

		FollowFrame();
		list = gpu.BeginCommandList( QueueType.Graphics, "Graphics block" );
		presentTo = null;
		context.Begin( list );
		FlushWrites();
		pendingShader = null;
		pendingAttributes = null;
		return RenderAttributes.CreateManaged();
	}

	public override void EndBlock()
	{
		try
		{
			context.FinishReadable();
			gpu.Submit( list, default, presentTo );
		}
		finally
		{
			list = null;
			presentTo = null;
			pendingAttributes = null;
		}
	}

	SwapChain presentTo;
	readonly Dictionary<SwapChain, ulong> acquiredIn = new();

	/// <summary>
	/// Call before a swap chain acquires: a second acquire in one device frame (a window drawn twice in an engine frame,
	/// from a resize) starts the next device frame, since each frame slot has one acquire semaphore per swap chain.
	/// </summary>
	public void BeforeAcquire( SwapChain swapChain )
	{
		// The frame has to have started before the acquire, so the acquire's semaphore belongs to the frame whose submit
		// waits on it
		FollowFrame();
		if ( acquiredIn.TryGetValue( swapChain, out var last ) && last == gpu.FrameNumber ) gpu.BeginFrame();
		acquiredIn[swapChain] = gpu.FrameNumber;
	}

	/// <summary>Forget a swap chain that's going away.</summary>
	public void Forget( SwapChain swapChain ) => acquiredIn.Remove( swapChain );

	/// <summary>
	/// A swap chain on a window, presenting with vsync or as soon as it can.
	/// </summary>
	public SwapChain CreateSwapChain( SurfaceSource surface, int width, int height, bool vsync ) => gpu.CreateSwapChain( new SwapChainInfo
	{
		Surface = surface,
		Width = width,
		Height = height,
		PresentMode = vsync ? PresentMode.Fifo : PresentMode.Immediate,
		Linear = true,
	} );

	/// <summary>
	/// Copy a texture into the image <paramref name="swapChain"/> acquired, last thing in the block, which then submits for
	/// that image; present it after the block ends.
	/// </summary>
	public void CopyToSwapChain( Texture source, SwapChain swapChain )
	{
		_ = Recording;
		context.CopyToPresent( TextureOf( source ), swapChain.Current );
		presentTo = swapChain;
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

	/// <summary>How many textures were copied from Source 2's pixels, having nothing of their own to start from.</summary>
	public int CopiedFromSource2 { get; private set; }

	/// <summary>
	/// A texture's backing. A render target is made to match, cleared as Source 2 clears a new one. Anything else comes
	/// from its compiled file through Basalt.Source2, or from the pixels it was made with, as RGBA8; failing both (a
	/// texture changed since, or made before this device), from mip 0 of Source 2's copy.
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

			// A target made with pixels - an image whose mips Source 2 generates - starts with them
			if ( InitialPixels( texture, info.Format ) is { } pixels )
			{
				gpu.WriteTexture( handle, pixels );
				if ( info.Mips > 1 ) FillMips( handle, info );
				context.Track( handle, ImageLayout.ShaderRead );
			}
			else
			{
				context.Track( handle, ImageLayout.Undefined );
				if ( texture.InitialColor is { } clear ) ClearNow( handle, clear );
			}
		}
		else if ( FromFile( texture ) is { } file )
		{
			handle = file;
			context.Track( handle, ImageLayout.ShaderRead );
		}
		else if ( InitialPixels( texture, GpuFormat.RGBA8Unorm ) is { } pixels )
		{
			// With its mips, as Source 2 generates them from the first
			var info = TextureInfo.Texture2D( texture.Width, texture.Height, GpuFormat.RGBA8Unorm, Math.Max( texture.Mips, 1 ), $"Engine image {texture.Width}x{texture.Height}" );
			handle = gpu.CreateTexture( info );
			gpu.WriteTexture( handle, pixels );
			if ( info.Mips > 1 ) FillMips( handle, info );
			context.Track( handle, ImageLayout.ShaderRead );
		}
		else
		{
			handle = gpu.CreateTexture( TextureInfo.Texture2D( texture.Width, texture.Height, GpuFormat.RGBA8Unorm, name: $"Engine image {texture.Width}x{texture.Height}" ) );
			gpu.WriteTexture( handle, CopyFromSource2( texture ) );
			context.Track( handle, ImageLayout.ShaderRead );
		}

		textures.Add( texture, new Backing<TextureHandle>( handle ) );
		textureOwners[handle] = texture;
		return handle;
	}

	/// <summary>A texture loaded from a compiled <c>.vtex</c>, read from that file.</summary>
	TextureHandle? FromFile( Texture texture )
	{
		var path = texture.ResourcePath;
		if ( string.IsNullOrEmpty( path ) || !path.EndsWith( ".vtex", StringComparison.OrdinalIgnoreCase ) ) return null;
		if ( files.Find( path ) is not { } file ) return null;

		try
		{
			return TextureFile.Load( file ).Create( gpu, path );
		}
		catch ( NotSupportedException )
		{
			return null;
		}
	}

	/// <summary>
	/// Mip 0 of the pixels a texture was made with, in <paramref name="format"/> (RGBA8 or BGRA8), when it was made in one
	/// of those.
	/// </summary>
	static byte[] InitialPixels( Texture texture, GpuFormat format )
	{
		if ( texture.InitialPixels is not { } data ) return null;
		if ( texture.ImageFormat is not (ImageFormat.RGBA8888 or ImageFormat.BGRA8888) ) return null;

		var size = texture.Width * texture.Height * 4;
		if ( data.Length < size ) return null;

		var pixels = data.Length == size ? data : data[..size];
		var swap = (texture.ImageFormat == ImageFormat.BGRA8888) != (format == GpuFormat.BGRA8Unorm);
		if ( !swap ) return pixels;

		pixels = pixels == data ? (byte[])data.Clone() : pixels;
		for ( int i = 0; i < size; i += 4 ) (pixels[i], pixels[i + 2]) = (pixels[i + 2], pixels[i]);
		return pixels;
	}

	byte[] CopyFromSource2( Texture texture )
	{
		CopiedFromSource2++;
		return MemoryMarshal.AsBytes( texture.GetPixels().AsSpan() ).ToArray();
	}

	/// <summary>
	/// Fill mips 1 and down of a texture whose mip 0 is in <see cref="ImageLayout.ShaderRead"/> by halving each into the
	/// next with a linear blit (a box filter), as a texture made with only its first mip gets the rest. Leaves it all in
	/// <see cref="ImageLayout.ShaderRead"/>.
	/// </summary>
	void FillMips( TextureHandle handle, in TextureInfo info )
	{
		var mips = gpu.BeginCommandList( QueueType.Graphics, "Fill mips" );
		for ( int i = 1; i < info.Mips; i++ )
		{
			int sw = Math.Max( info.Width >> (i - 1), 1 ), sh = Math.Max( info.Height >> (i - 1), 1 );
			int dw = Math.Max( info.Width >> i, 1 ), dh = Math.Max( info.Height >> i, 1 );
			mips.Barrier( MipBarrier( handle, i - 1, ImageLayout.ShaderRead, ImageLayout.CopySource ) );
			mips.Barrier( MipBarrier( handle, i, ImageLayout.Undefined, ImageLayout.CopyDestination ) );
			mips.Blit( handle, (0, 0, sw, sh), handle, (0, 0, dw, dh), Filter.Linear, i - 1, i );
			mips.Barrier( MipBarrier( handle, i - 1, ImageLayout.CopySource, ImageLayout.ShaderRead ) );
			mips.Barrier( MipBarrier( handle, i, ImageLayout.CopyDestination, ImageLayout.ShaderRead ) );
		}

		gpu.Submit( mips );
	}

	static TextureBarrier MipBarrier( TextureHandle texture, int mip, ImageLayout from, ImageLayout to ) => new()
	{
		Texture = texture,
		OldLayout = from,
		NewLayout = to,
		SourceStage = PipelineStage.AllCommands,
		SourceAccess = from == ImageLayout.Undefined ? MemoryAccess.None : MemoryAccess.MemoryWrite,
		DestinationStage = PipelineStage.AllCommands,
		DestinationAccess = MemoryAccess.MemoryRead | MemoryAccess.MemoryWrite,
		BaseMip = mip,
		MipCount = 1,
		LayerCount = 1,
	};

	/// <summary>Clear a new target now, in the block being recorded or on its own.</summary>
	void ClearNow( TextureHandle handle, Color color )
	{
		var value = new Num.Vector4( color.r, color.g, color.b, color.a );
		if ( list is not null )
		{
			context.ClearTexture( handle, value );
			return;
		}

		var clear = gpu.BeginCommandList( QueueType.Graphics, "Clear new target" );
		context.Begin( clear );
		context.ClearTexture( handle, value );
		context.Finish();
		gpu.Submit( clear );
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

		// In a block, in order with its draws
		if ( list is not null )
		{
			context.UpdateBuffer( handle, bytes, offset );
			return;
		}

		// Outside one - a recording uploading its tables - at the start of the next block, which is what draws with
		// them: writing now would submit and wait for every upload
		var copy = System.Buffers.ArrayPool<byte>.Shared.Rent( size );
		bytes.CopyTo( copy );
		writes.Add( (handle, copy, size, offset) );
	}

	readonly List<(BufferHandle Buffer, byte[] Data, int Size, int Offset)> writes = new();
	readonly HashSet<BufferHandle> written = new();

	/// <summary>Record the buffer writes made outside a block, in order, at the start of this one.</summary>
	void FlushWrites()
	{
		if ( writes.Count == 0 ) return;

		written.Clear();
		foreach ( var (buffer, data, size, offset) in writes )
		{
			if ( ownedBuffers.Contains( buffer ) )
			{
				// A second write to a buffer waits for the first, in case they overlap
				if ( !written.Add( buffer ) )
				{
					list.Barrier( PipelineStage.Transfer, MemoryAccess.TransferWrite, PipelineStage.Transfer, MemoryAccess.TransferWrite );
					written.Clear();
					written.Add( buffer );
				}

				list.UpdateBuffer( buffer, data.AsSpan( 0, size ), offset );
			}

			System.Buffers.ArrayPool<byte>.Shared.Return( data );
		}

		writes.Clear();
		context.MemoryBarrier();
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

		foreach ( var write in writes ) System.Buffers.ArrayPool<byte>.Shared.Return( write.Data );
		writes.Clear();

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
