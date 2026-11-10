using Basalt.Gpu;
using Basalt.Source2;
using Basalt.Source2.Materials;
using Basalt.Source2.Shaders;
using System.Diagnostics;
using System.Threading;

namespace Basalt.Backend;

/// <summary>
/// Basalt's <see cref="GpuDevice"/> on <see cref="Basalt.Gpu.Device"/>: resources map one-to-one onto Basalt.Gpu's, and
/// materials are s&amp;box's own - compiled shaders, materials and textures read by Basalt.Source2 from the game's
/// content. What it can't do yet (skinning, the sky, engine compute shaders) draws nothing. See
/// docs/managed/basalt-gpu.md.
/// </summary>
public sealed class BackendDevice : GpuDevice, IDisposable
{
	readonly Stopwatch clock = Stopwatch.StartNew();
	readonly Dictionary<string, GpuTexture> loadedTextures = new( StringComparer.OrdinalIgnoreCase );
	readonly Dictionary<string, GpuMaterial> sharedMaterials = new( StringComparer.OrdinalIgnoreCase );
	readonly Dictionary<IntPtr, SyncPoint> semaphores = new();
	readonly Dictionary<IntPtr, BufferHandle> constantBuffers = new();
	readonly List<(SyncPoint Point, BufferHandle Buffer, int Bytes, GpuReadback Callback)> readbacks = new();
	readonly Dictionary<PipelineKey, PipelineHandle> pipelines = new();
	readonly Dictionary<VertexLayout, VertexLayout> layouts = new();
	long nextHandle;
	int vertexCache;

	/// <summary>The Basalt.Gpu device it renders with.</summary>
	public Device Gpu { get; }

	/// <summary>Where compiled shaders, materials and textures are read from.</summary>
	public ContentFiles Content { get; }

	/// <summary>s&amp;box's compiled shaders, their modules and binding layouts.</summary>
	internal ShaderLibrary Shaders { get; }

	/// <summary>What's bound where nothing else is: black textures, zeroed buffers, the default vertex stream.</summary>
	internal ShaderDefaults Defaults { get; }

	readonly BackendTexture black;
	readonly BackendTexture sheetMetadata;

	/// <summary>
	/// Render Basalt on <paramref name="gpu"/>, which stays the caller's, with compiled content from
	/// <paramref name="content"/> (by default the checkout's <c>game/core</c> above the application).
	/// </summary>
	public BackendDevice( Device gpu, ContentFiles content = null )
	{
		Gpu = gpu;
		Content = content ?? ContentFiles.FindGame();
		Shaders = new ShaderLibrary( gpu, Content );
		Defaults = new ShaderDefaults( gpu );

		// Bindless buffers shaders may read: nothing animated, no pipeline texture indices
		gpu.SetBindlessBuffer( ShaderAbi.BindlessVertexCache, Defaults.Storage );
		gpu.SetBindlessBuffer( ShaderAbi.BindlessPipelineTextures, Defaults.Storage );

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
	/// A compiled texture (<c>.vtex_c</c>) from the content, loaded once; a black 1x1 stand-in (a cube for cube paths)
	/// when it isn't there or its format has no Basalt.Gpu form.
	/// </summary>
	public override GpuTexture LoadTexture( string path )
	{
		lock ( loadedTextures )
		{
			if ( loadedTextures.TryGetValue( path, out var texture ) ) return texture;
			return loadedTextures[path] = Load( path ) ?? Placeholder( path, cube: path.Contains( "cube", StringComparison.OrdinalIgnoreCase ) );
		}
	}

	BackendTexture Load( string path )
	{
		var file = Content.Find( path );
		if ( file is null ) return null;

		var texture = TextureFile.Load( file );
		if ( texture.Format == Format.Unknown ) return null;

		var desc = new TextureDesc
		{
			Width = texture.Width,
			Height = texture.Height,
			Depth = texture.Depth,
			Mips = texture.Mips,
			Samples = 1,
			Cube = texture.Dimension is TextureDimension.TextureCube,
			Format = TextureFormat.RGBA8888,
		};

		return new BackendTexture( this, texture.Create( Gpu, path ), desc, path ) { Layout = ImageLayout.ShaderRead, Dimension = texture.Dimension };
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
	/// A compiled material (<c>.vmat_c</c>) with its shader and textures; one that isn't there, or whose shader isn't,
	/// draws nothing.
	/// </summary>
	public override GpuMaterial LoadMaterial( string path )
	{
		lock ( sharedMaterials )
		{
			if ( sharedMaterials.TryGetValue( path, out var material ) ) return material;
			return sharedMaterials[path] = Material( path, null );
		}
	}

	/// <summary>A material that's a shader with its defaults.</summary>
	public override GpuMaterial MaterialFromShader( string shaderPath )
	{
		lock ( sharedMaterials )
		{
			if ( sharedMaterials.TryGetValue( shaderPath, out var material ) ) return material;
			return sharedMaterials[shaderPath] = new BackendMaterial( shaderPath, Shaders.Load( shaderPath ), null, null );
		}
	}

	/// <inheritdoc/>
	public override GpuMaterial CreateMaterial( string name, string shaderPath ) => new BackendMaterial( name, Shaders.Load( shaderPath ), null, null );

	/// <summary>
	/// A lit material of one colour, for meshes made with <see cref="BackendMesh.Create"/>:
	/// <c>materials/default/white.vmat</c> (complex.shader, white, flat normal) tinted with <paramref name="albedo"/>.
	/// </summary>
	public GpuMaterial CreateMaterial( string name, Color albedo )
	{
		var values = new Dictionary<string, float[]>( StringComparer.OrdinalIgnoreCase ) { ["g_vColorTint"] = [albedo.r, albedo.g, albedo.b, 0] };
		return Content.Find( "materials/default/white.vmat" ) is null
			? new BackendMaterial( name, Shaders.Load( "shaders/complex.shader" ), null, null, values )
			: Material( "materials/default/white.vmat", values, name );
	}

	/// <summary>
	/// A compiled material with <paramref name="values"/> (shader variables by name) over its own. It draws nothing if
	/// it, or its shader, isn't built.
	/// </summary>
	public GpuMaterial LoadMaterial( string path, IReadOnlyDictionary<string, float[]> values, string name = null )
		=> Material( path, values is null ? null : new( values, StringComparer.OrdinalIgnoreCase ), name );

	BackendMaterial Material( string path, Dictionary<string, float[]> values, string name = null )
	{
		var file = Content.Find( path ) is { } found ? MaterialFile.Load( found ) : null;
		if ( file is null ) return new BackendMaterial( name ?? path, null, null, null, values );

		var textures = new Dictionary<string, BackendTexture>( StringComparer.OrdinalIgnoreCase );
		foreach ( var (variable, texture) in file.Textures )
		{
			if ( LoadTexture( texture ) is BackendTexture loaded ) textures[variable] = loaded;
		}

		return new BackendMaterial( name ?? path, Shaders.Load( file.Shader ), file, textures, values );
	}

	/// <summary>
	/// Compiled models: a <c>.vmdl_c</c> from the content as a mesh Basalt draws, with its LODs and materials, or null
	/// when it isn't built. See <see cref="BackendModel.Load"/>.
	/// </summary>
	public RenderMesh LoadModel( string path, ulong? meshGroups = null, int materialGroup = 0 ) => BackendModel.Load( this, path, meshGroups, materialGroup );

	/// <summary>One instance of each distinct vertex layout, which pipelines are keyed by.</summary>
	internal VertexLayout Layout( VertexLayout layout )
	{
		lock ( layouts )
		{
			if ( layouts.TryGetValue( layout, out var existing ) ) return existing;
			layouts[layout] = layout;
			return layout;
		}
	}

	/// <summary>
	/// A pipeline for a variant drawing vertices laid out as <paramref name="layout"/> into targets of these formats,
	/// made once. Mesh vertices are stream 0, instance ids stream 1 (<c>TEXCOORD13</c>), and inputs the mesh lacks, or
	/// has as integers where the shader wants floats (or the other way round), read the default stream 2, as native's
	/// input layouts do for streams a mesh doesn't have.
	/// </summary>
	internal PipelineHandle Pipeline( ShaderVariant variant, VertexLayout layout, Format color, Format depth, int samples, bool depthOnly )
	{
		var key = new PipelineKey( variant, layout, color, depth, samples, depthOnly );
		lock ( pipelines )
		{
			if ( pipelines.TryGetValue( key, out var pipeline ) ) return pipeline;

			var attributes = ShaderDefaults.MapVertices( variant.VertexInputs, input =>
			{
				if ( input.Semantic == VertexSemantic.TexCoord && input.SemanticIndex == 13 ) return new VertexAttribute( 0, 1, VertexFormat.UInt, 0 );
				if ( layout.Find( input.Semantic, input.SemanticIndex ) is not { } field ) return null;
				var integer = field.Format is VertexFormat.UByte4 or VertexFormat.UInt or VertexFormat.UInt2 or VertexFormat.UInt4;
				if ( integer != (input.Kind != SpirvScalar.Float) ) return null;
				return new VertexAttribute( 0, 0, field.Format, field.Offset );
			}, defaultBuffer: 2 );

			VertexBufferLayout[] streams = [new( layout.Stride ), new( 4, VertexStepRate.Instance ), new( 0 )];
			var info = variant.PipelineInfo( streams, attributes, color == Format.Unknown ? [] : [color], depth, samples, depthBias: depthOnly, $"{variant}{(depthOnly ? " depth" : "")}" );

			// Stripped pixel shaders: depth passes of opaque materials don't need them
			if ( depthOnly && color == Format.Unknown )
			{
				info.FragmentShader = default;
				info.FragmentEntry = null;
				info.FragmentConstants = null;
			}

			pipeline = Gpu.CreateGraphicsPipeline( info );
			pipelines[key] = pipeline;
			return pipeline;
		}
	}

	internal readonly record struct PipelineKey( ShaderVariant Variant, VertexLayout Layout, Format Color, Format Depth, int Samples, bool DepthOnly );

	/// <summary>A compute pipeline for a compute shader's variant, made once.</summary>
	internal PipelineHandle ComputePipeline( ShaderVariant variant )
	{
		var key = new PipelineKey( variant, null, Format.Unknown, Format.Unknown, 0, false );
		lock ( pipelines )
		{
			if ( pipelines.TryGetValue( key, out var pipeline ) ) return pipeline;
			pipeline = Gpu.CreateComputePipeline( new ComputePipelineInfo
			{
				Shader = variant.Compute.Module,
				Entry = variant.Compute.Bytecode.EntryPoint( ShaderProgramType.Compute ),
				Constants = variant.Compute.Constants,
				Sets = variant.Sets,
				Name = variant.ToString(),
			} );
			pipelines[key] = pipeline;
			return pipeline;
		}
	}

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
		lock ( pipelines )
		{
			foreach ( var pipeline in pipelines.Values ) Gpu.Destroy( pipeline );
			pipelines.Clear();
		}

		Shaders.Dispose();
		Defaults.Dispose();
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
