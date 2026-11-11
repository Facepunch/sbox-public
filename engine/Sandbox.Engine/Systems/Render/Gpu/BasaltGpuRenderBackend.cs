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
	readonly BackendDevice services;
	readonly ContentFiles files;
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
		SubmitUploads();
		gpu.BeginFrame();
	}

	BasaltGpuRenderBackend( GpuDevice gpu, bool ownsDevice )
	{
		this.gpu = gpu;
		this.ownsDevice = ownsDevice;
		files = ContentFiles.FindGame();
		services = new BackendDevice( gpu, files );
		drawer = new ShaderDrawer( services );
		context = new GraphicsContext( gpu, drawer, null );
		uploadKey = new IntPtr( -1 - System.Threading.Interlocked.Increment( ref nextKey ) );
		Register( this );
	}

	/// <summary>The device everything records on.</summary>
	public GpuDevice Device => gpu;

	/// <summary>The context blocks record through.</summary>
	public GraphicsContext Context => context;

	/// <summary>
	/// The renderer services draws bind through: the device's shaders, placeholders and material objects, shared with
	/// Basalt's scene on this device.
	/// </summary>
	public BackendDevice Services => services;

	/// <summary>Bind through material objects (the default); off binds by reflection every draw, for comparisons.</summary>
	public bool MaterialObjects
	{
		get => drawer.MaterialObjects;
		set => drawer.MaterialObjects = value;
	}

	/// <summary>Material objects made, sets written through them and held now, and draws that fell back to reflection.</summary>
	public (int Objects, int SetsWritten, int SetsHeld, int Fallbacks) MaterialObjectStats
	{
		get
		{
			var (objects, written, held) = drawer.MaterialObjectStats;
			return (objects, written, held, drawer.Fallbacks);
		}
	}

	//
	// Blocks and resources
	//

	public override RenderAttributes BeginBlock()
	{
		if ( list is not null ) throw new InvalidOperationException( "A Basalt.Gpu block is already recording." );

		FollowFrame();
		SubmitUploads();
		list = gpu.BeginCommandList( QueueType.Graphics, "Graphics block" );
		presentTo = null;
		context.Begin( list );
		pendingShader = null;
		pendingAttributes = null;
		return blockAttributes = RenderAttributes.CreateManaged();
	}

	// The block's attributes, which a draw's own are layered over, as Source 2's contexts layer them
	RenderAttributes blockAttributes;

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
			blockAttributes = null;
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
		if ( acquiredIn.TryGetValue( swapChain, out var last ) && last == gpu.FrameNumber )
		{
			SubmitUploads();
			gpu.BeginFrame();
		}

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
				context.Forget( backing.Handle );

				// A borrowed backing (a Basalt frame's target) stays its owner's
				if ( textureOwners.Remove( backing.Handle ) ) gpu.Destroy( backing.Handle );
				break;

			case GpuBuffer buffer when buffers.TryGetValue( buffer, out var backing ):
				// Writes waiting for the next block may name it: submit them first, so it's freed after they've run
				SubmitUploads();
				buffers.Remove( buffer );
				ownedBuffers.Remove( backing.Handle );
				gpu.Destroy( backing.Handle );
				break;
		}
	}

	/// <summary>
	/// Make <paramref name="handle"/>, a texture on this device that something else owns (a Basalt frame's target, now in
	/// <paramref name="layout"/>), the backing of <paramref name="texture"/>: drawing with the engine texture draws with it,
	/// and it isn't destroyed with the engine texture or this backend. A backing the texture had is released first.
	/// </summary>
	internal void Borrow( Texture texture, TextureHandle handle, ImageLayout layout )
	{
		if ( textures.TryGetValue( texture, out var backing ) )
		{
			if ( backing.Handle == handle )
			{
				context.Track( handle, layout );
				return;
			}

			Release( texture );
		}

		textures.Add( texture, new Backing<TextureHandle>( handle ) );
		context.Track( handle, layout );
	}

	/// <summary>The layout an engine texture's backing is in, as the recording left it.</summary>
	internal ImageLayout LayoutOf( Texture texture ) => textures.TryGetValue( texture, out var backing ) ? context.LayoutOf( backing.Handle ) : ImageLayout.Undefined;

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

			// A target made with pixels - an image whose mips Source 2 generates, or one made for compute - starts with them
			if ( (InitialPixels( texture, info.Format ) ?? (RawPixels( texture ) is { } raw && raw.Format == info.Format ? raw.Data : null)) is { } pixels )
			{
				Upload( handle, info, pixels, ImageLayout.Undefined );
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
		}
		else if ( InitialPixels( texture, GpuFormat.RGBA8Unorm ) is { } pixels )
		{
			// With its mips, as Source 2 generates them from the first
			var info = TextureInfo.Texture2D( texture.Width, texture.Height, GpuFormat.RGBA8Unorm, Math.Max( texture.Mips, 1 ), $"Engine image {texture.Width}x{texture.Height}" );
			handle = gpu.CreateTexture( info );
			Upload( handle, info, pixels, ImageLayout.Undefined );
		}
		else if ( RawPixels( texture ) is { } raw )
		{
			// Another format made with its first mip's data (float images, lookup tables): the same bytes, and storage
			// when it was made for compute
			var info = TextureInfo.Texture2D( texture.Width, texture.Height, raw.Format, Math.Max( texture.Mips, 1 ), $"Engine image {texture.Width}x{texture.Height}" );
			if ( texture.Desc.m_nFlags.Contains( NativeEngine.RuntimeTextureSpecificationFlags.TSPEC_UAV ) ) info.Usage |= TextureUsage.Storage;
			handle = gpu.CreateTexture( info );
			Upload( handle, info, raw.Data, ImageLayout.Undefined );
		}
		else
		{
			var info = TextureInfo.Texture2D( texture.Width, texture.Height, GpuFormat.RGBA8Unorm, name: $"Engine image {texture.Width}x{texture.Height}" );
			handle = gpu.CreateTexture( info );
			Upload( handle, info, CopyFromSource2( texture ), ImageLayout.Undefined );
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
			var handle = TextureFile.Load( file ).Create( gpu, path, UploadList() );
			context.Track( handle, ImageLayout.ShaderRead );
			return handle;
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

	/// <summary>
	/// Mip 0 of the pixels a texture was made with, as they are, when its format has a Basalt.Gpu form and they're at
	/// least one mip's worth.
	/// </summary>
	static (GpuFormat Format, byte[] Data)? RawPixels( Texture texture )
	{
		if ( texture.InitialPixels is not { } data || texture.Depth > 1 ) return null;
		var format = DataFormatOf( texture.ImageFormat );
		if ( format == GpuFormat.Unknown ) return null;
		var size = texture.Width * texture.Height * format.BytesPerBlock();
		if ( data.Length < size ) return null;
		return (format, data.Length == size ? data : data[..size]);
	}

	/// <summary>The Basalt.Gpu form of an uncompressed engine format, or Unknown.</summary>
	static GpuFormat DataFormatOf( ImageFormat format ) => format switch
	{
		ImageFormat.RGBA8888 => GpuFormat.RGBA8Unorm,
		ImageFormat.BGRA8888 => GpuFormat.BGRA8Unorm,
		ImageFormat.A8 or ImageFormat.I8 => GpuFormat.R8Unorm,
		ImageFormat.R32F => GpuFormat.R32Float,
		ImageFormat.RG3232F => GpuFormat.RG32Float,
		ImageFormat.RGBA32323232F => GpuFormat.RGBA32Float,
		ImageFormat.RGBA16161616F => GpuFormat.RGBA16Float,
		ImageFormat.RG1616F => GpuFormat.RG16Float,
		ImageFormat.I16F => GpuFormat.R16Float,
		ImageFormat.R11G11B10F => GpuFormat.RG11B10Float,
		ImageFormat.RGBA1010102 => GpuFormat.RGB10A2Unorm,
		_ => GpuFormat.Unknown,
	};

	byte[] CopyFromSource2( Texture texture )
	{
		CopiedFromSource2++;
		return MemoryMarshal.AsBytes( texture.GetPixels().AsSpan() ).ToArray();
	}

	/// <summary>
	/// Write mip 0 of a texture (now in <paramref name="from"/>) and fill the rest from it, recorded where uploads go -
	/// the block being recorded, or the list of uploads submitted before the next block - rather than submitted and
	/// waited for. The texture ends in <see cref="ImageLayout.ShaderRead"/>.
	/// </summary>
	void Upload( TextureHandle handle, in TextureInfo info, ReadOnlySpan<byte> pixels, ImageLayout from, int x = 0, int y = 0, int width = 0, int height = 0 )
	{
		var target = UploadList();
		target.Barrier( new TextureBarrier
		{
			Texture = handle,
			OldLayout = from,
			NewLayout = ImageLayout.CopyDestination,
			SourceStage = PipelineStage.AllCommands,
			SourceAccess = from == ImageLayout.Undefined ? MemoryAccess.None : MemoryAccess.MemoryWrite,
			DestinationStage = PipelineStage.Transfer,
			DestinationAccess = MemoryAccess.TransferWrite,
		} );

		target.UpdateTexture( handle, pixels, 0, 0, width > 0 ? width : info.Width, height > 0 ? height : info.Height, x, y );
		target.Barrier( MipBarrier( handle, 0, ImageLayout.CopyDestination, ImageLayout.ShaderRead ) );
		if ( info.Mips > 1 ) FillMips( target, handle, info, ImageLayout.CopyDestination );
		context.Track( handle, ImageLayout.ShaderRead );
		TextureUploads++;
	}

	/// <summary>Texture writes recorded rather than submitted and waited for, for tests.</summary>
	public int TextureUploads { get; private set; }

	/// <summary>
	/// Where a write is recorded: the block being recorded (outside any rendering scope), or the list of uploads
	/// submitted before the next one.
	/// </summary>
	GpuCommandList UploadList()
	{
		if ( list is not null )
		{
			context.MemoryBarrier();
			return list;
		}

		if ( uploads is null )
		{
			FollowFrame();
			uploads = gpu.BeginCommandList( QueueType.Graphics, "Engine uploads" );
			written.Clear();
		}

		return uploads;
	}

	/// <summary>
	/// Fill mips 1 and down of a texture whose mip 0 is in <see cref="ImageLayout.ShaderRead"/> (and the rest in
	/// <paramref name="rest"/>) by halving each into the next with a linear blit (a box filter), as a texture made with
	/// only its first mip gets the rest. Leaves it all in <see cref="ImageLayout.ShaderRead"/>.
	/// </summary>
	void FillMips( GpuCommandList mips, TextureHandle handle, in TextureInfo info, ImageLayout rest )
	{
		var filter = info.Format is GpuFormat.R32Float or GpuFormat.RG32Float or GpuFormat.RGBA32Float ? Filter.Nearest : Filter.Linear;
		for ( int i = 1; i < info.Mips; i++ )
		{
			int sw = Math.Max( info.Width >> (i - 1), 1 ), sh = Math.Max( info.Height >> (i - 1), 1 );
			int dw = Math.Max( info.Width >> i, 1 ), dh = Math.Max( info.Height >> i, 1 );
			mips.Barrier( MipBarrier( handle, i - 1, ImageLayout.ShaderRead, ImageLayout.CopySource ) );
			mips.Barrier( MipBarrier( handle, i, rest, ImageLayout.CopyDestination ) );
			mips.Blit( handle, (0, 0, sw, sh), handle, (0, 0, dw, dh), filter, i - 1, i );
			mips.Barrier( MipBarrier( handle, i - 1, ImageLayout.CopySource, ImageLayout.ShaderRead ) );
			mips.Barrier( MipBarrier( handle, i, ImageLayout.CopyDestination, ImageLayout.ShaderRead ) );
		}
	}

	/// <summary>
	/// A texture updated on Source 2's device: its copy here takes the same bytes, recorded where uploads go, rather than
	/// being dropped and copied back from Source 2's pixels. Mips past the first are filled again from it. False when the
	/// copy is in another format than the data, or the update is of a 3D texture.
	/// </summary>
	public override bool Update( Texture texture, ReadOnlySpan<byte> data, int x, int y, int z, int width, int height, int depth )
	{
		if ( !textures.TryGetValue( texture, out var backing ) ) return true;
		if ( z != 0 || depth > 1 ) return false;

		var info = gpu.InfoOf( backing.Handle );
		if ( DataFormatOf( texture.ImageFormat ) != info.Format ) return false;

		width = width > 0 ? width : info.Width;
		height = height > 0 ? height : info.Height;
		var size = width * height * info.Format.BytesPerBlock();
		if ( data.Length < size ) return false;

		Upload( backing.Handle, info, data[..size], context.LayoutOf( backing.Handle ), x, y, width, height );
		return true;
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

	static GpuFormat FormatOf( ImageFormat format ) => format is not (ImageFormat.A8 or ImageFormat.I8) && DataFormatOf( format ) is var mapped && mapped != GpuFormat.Unknown
		? mapped
		: throw new NotSupportedException( $"Render target format {format} on Basalt.Gpu" );

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

		// Compute shaders name their path under shaders/ (postprocess/postprocess_bloom_cs)
		shader = drawer.Shaders.Load( file ) ?? (file.StartsWith( "shaders/", StringComparison.OrdinalIgnoreCase ) ? null : drawer.Shaders.Load( "shaders/" + file ));
		shaders[path] = shader;
		if ( shader is not null ) AddNames( shader );
		return shader;
	}

	ShaderFile Shader( string path ) => shaders.TryGetValue( path, out var shader ) ? shader : shaders[path] = AddNames( drawer.Shaders.Load( path ) );

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
		if ( attributes?.Managed is null )
			throw new NotSupportedException( "Native render attributes can't be drawn with on Basalt.Gpu" );

		// A draw's attributes over the block's: a post process's blit reads the frame grab its command list set on the block
		if ( blockAttributes?.Managed is { } block && !ReferenceEquals( block, attributes.Managed ) ) Lower( block );
		Lower( attributes.Managed );
		return lowered;
	}

	void Lower( ManagedRenderAttributes managed )
	{
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
				case ManagedRenderAttributes.TextureValue { Texture: { } texture, Mip: var mip }:
					lowered.Set( name, context.View( TextureOf( texture ), mip ) );
					break;
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

	public override void CopyTexture( Texture source, Texture destination, int sourceMip, int sourceSlice, int destinationMip, int destinationSlice )
	{
		_ = Recording;
		if ( sourceMip != destinationMip || sourceSlice != 0 || destinationSlice != 0 ) throw Unsupported( "Texture copies between mips or slices" );
		context.CopyTexture( TextureOf( source ), TextureOf( destination ), sourceMip );
	}

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
		switch ( method )
		{
			case Graphics.DownsampleMethod.GaussianBlur or Graphics.DownsampleMethod.GaussianBorder:
				context.GenerateMips( TextureOf( texture ), Shader( "shaders/downsample_cs.shader" ), (int)method, initialMip );
				break;

			case Graphics.DownsampleMethod.GGX:
				throw Unsupported( "GGX mips" );

			// Box, min, max and min-max: downsample_spd, six mips a pass, as Source 2 runs them
			default:
				context.DownsampleSpd( TextureOf( texture ), Shader( "shaders/downsample_spd.shader" ), (int)method, initialMip );
				break;
		}
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

		// Outside one - a recording uploading its tables - copied once, into the upload memory of a list of their own that's
		// submitted before the next block, which is what draws with them: writing now would submit and wait for each
		if ( uploads is null )
		{
			FollowFrame();
			uploads = gpu.BeginCommandList( QueueType.Graphics, "Engine uploads" );
			written.Clear();
		}

		// A second write to a buffer waits for the first, in case they overlap
		if ( !written.Add( handle ) )
		{
			uploads.Barrier( PipelineStage.Transfer, MemoryAccess.TransferWrite, PipelineStage.Transfer, MemoryAccess.TransferWrite );
			written.Clear();
			written.Add( handle );
		}

		uploads.UpdateBuffer( handle, bytes, offset );
		UploadsRecorded++;
	}

	GpuCommandList uploads;
	readonly HashSet<BufferHandle> written = new();

	/// <summary>Buffer writes recorded outside blocks, for tests.</summary>
	public int UploadsRecorded { get; private set; }

	/// <summary>
	/// Submit the buffer writes made outside a block, visible to everything after them: before a block, a device frame,
	/// a readback, or freeing a buffer they may write.
	/// </summary>
	void SubmitUploads()
	{
		if ( uploads is null ) return;
		uploads.Barrier( PipelineStage.Transfer, MemoryAccess.TransferWrite, PipelineStage.AllCommands, MemoryAccess.MemoryRead | MemoryAccess.MemoryWrite );
		gpu.Submit( uploads );
		uploads = null;
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
	/// A mip of a texture's backing as bytes, in its Basalt.Gpu format, after everything submitted so far.
	/// </summary>
	public byte[] ReadBytes( Texture texture, int mip = 0 )
	{
		var handle = TextureOf( texture );
		SubmitUploads();
		var info = gpu.InfoOf( handle );
		var size = Math.Max( info.Width >> mip, 1 ) * Math.Max( info.Height >> mip, 1 ) * info.Format.BytesPerBlock();
		var readback = gpu.CreateBuffer( new BufferInfo { Size = size, Memory = MemoryLocation.Readback, Name = "Readback" } );
		try
		{
			var copy = gpu.BeginCommandList( QueueType.Graphics, "Readback" );
			context.Begin( copy );
			context.CopyToBuffer( handle, readback, mip );
			context.Finish();
			gpu.Wait( gpu.Submit( copy ) );
			return gpu.Map( readback )[..size].ToArray();
		}
		finally
		{
			gpu.Destroy( readback );
		}
	}

	/// <summary>
	/// Mip 0 of an RGBA8 texture's backing, after everything submitted so far: what the device drew into it.
	/// </summary>
	public Color32[] ReadPixels( Texture texture )
	{
		var handle = TextureOf( texture );
		var info = gpu.InfoOf( handle );
		if ( info.Format is not (GpuFormat.RGBA8Unorm or GpuFormat.RGBA8Srgb) ) throw new NotSupportedException( $"Reading {info.Format} back" );

		SubmitUploads();
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
	public void NextFrame()
	{
		SubmitUploads();
		gpu.BeginFrame();
	}

	public void Dispose()
	{
		Unregister( this );
		SubmitUploads();
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
		drawer.Dispose();
		services.Dispose();
		if ( ownsDevice ) gpu.Dispose();
	}
}
