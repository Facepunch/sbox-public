using Basalt.Gpu;
using Basalt.Source2;
using Basalt.Source2.Materials;
using Basalt.Source2.Shaders;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Basalt.Backend;

/// <summary>
/// Basalt's <see cref="GpuDevice"/> on <see cref="Basalt.Gpu.Device"/>: resources map one-to-one onto Basalt.Gpu's, and
/// materials are s&amp;box's own - compiled shaders, materials and textures read by Basalt.Source2 from the game's
/// content. What it can't do yet (morphs, engine compute shaders with parameters) draws nothing. See
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

	/// <summary>
	/// Draw models and dispatch compute through material objects (persistent sets, constants written once), which is the
	/// default. Off binds by reflection every draw: the reference the tests compare against. See
	/// docs/managed/basalt-materials.md.
	/// </summary>
	public bool MaterialObjects { get; set; } = true;

	/// <summary>
	/// Depth-only draws whose vertex shader reads nothing but positions fetch them from a positions-only copy of the mesh's
	/// vertices (12 bytes a vertex) rather than the full vertices: on Construct the prepass reads 72 MB instead of 158 MB and
	/// is 0.15 ms faster. Models loaded while it's on get the copy (<see cref="PositionStreamBytes"/>).
	/// </summary>
	public bool PositionStreams { get; set; } = true;

	/// <summary>Bytes of positions-only vertex copies made (<see cref="PositionStreams"/>).</summary>
	public long PositionStreamBytes;

	/// <summary>The material objects drawn so far: the one material binding service of this device.</summary>
	internal MaterialObjectCache MaterialCache
	{
		get
		{
			if ( materialCache is { } cache ) return cache;
			lock ( pipelines ) return materialCache ??= new MaterialObjectCache( Gpu, Defaults );
		}
	}

	MaterialObjectCache materialCache;

	/// <summary>Material objects made so far and descriptor sets they wrote, for benchmarks and tests.</summary>
	public (int Objects, int SetsWritten) MaterialObjectStats => materialCache is null ? default : (materialCache.Count, materialCache.SetsWritten);

	/// <summary>Drop a material's objects; they're made again when it next draws. Changing a material does this itself.</summary>
	public void InvalidateMaterial( GpuMaterial material ) => materialCache?.Invalidate( material );

	/// <summary>
	/// Read a loaded material's compiled file (and its shader and textures) again, after it changed on disk; draws pick
	/// the new one up. Values set in code stay. Call between frames. False when it isn't loaded.
	/// </summary>
	public bool ReloadMaterial( string path )
	{
		GpuMaterial loaded;
		lock ( sharedMaterials )
		{
			if ( !sharedMaterials.TryGetValue( path, out loaded ) ) return false;
		}

		if ( loaded is not BackendMaterial material ) return false;
		var file = Content.TryRead( path ) is { } data ? MaterialFile.Read( data ) : null;
		material.Reload( file is null ? null : Shaders.Load( file.Shader ), file, file is null ? null : Textures( file ) );
		return true;
	}

	Dictionary<string, BackendTexture> Textures( MaterialFile file )
	{
		var textures = new Dictionary<string, BackendTexture>( StringComparer.OrdinalIgnoreCase );
		foreach ( var (variable, texture) in file.Textures )
		{
			if ( LoadTexture( texture ) is BackendTexture loaded ) textures[variable] = loaded;
		}

		return textures;
	}

	BackendMaterial Track( BackendMaterial material )
	{
		material.Disposing = m => materialCache?.Invalidate( m );
		return material;
	}

	/// <summary>
	/// New-tier shaders (Slang surfaces and raw modules) and their materials' records, compiled when a
	/// <see cref="SurfaceMaterial"/> of theirs first draws. See docs/managed/shader-format-replacement.md.
	/// </summary>
	public SurfaceShaders Surfaces
	{
		get
		{
			if ( surfaces is { } made ) return made;
			lock ( pipelines ) return surfaces ??= new SurfaceShaders( this );
		}
	}

	SurfaceShaders surfaces;

	/// <summary>
	/// Keep static models' geometry in GPU-driven pages as they load (<see cref="GpuDrivenTables"/>), so
	/// <see cref="GpuDriven"/> can draw them; the classic path draws from the same pages. Set before loading models; on
	/// from the start with <see cref="GpuDrivenByDefault"/>.
	/// </summary>
	public bool GpuDrivenGeometry
	{
		get => gpuDrivenTables is not null;
		set
		{
			if ( value && gpuDrivenTables is null ) gpuDrivenTables = new GpuDrivenTables( Gpu );
		}
	}

	GpuDrivenTables gpuDrivenTables;

	/// <summary>The GPU-driven pages and tables, or null when <see cref="GpuDrivenGeometry"/> is off.</summary>
	internal GpuDrivenTables GpuDrivenTables => gpuDrivenTables;

	/// <summary>
	/// Draw aggregates GPU-driven: shaders' <c>.gpudriven.shader_c</c> variants, per-draw data in tables, one multi-draw
	/// per pipeline and pass state (docs/managed/basalt-gpu-driven.md). Needs <see cref="GpuDrivenGeometry"/> and material
	/// objects; anything it can't draw goes the classic way. On by default (<see cref="GpuDrivenByDefault"/>).
	/// </summary>
	public bool GpuDriven { get; set; }

	/// <summary>The GPU-driven tables' records and bytes, for reports.</summary>
	public string GpuDrivenReport => gpuDrivenTables is not { } t ? "off" : $"{t.Records.Geometry} geometry, {t.Records.Draws} draw and {t.Records.Materials} material records ({t.Bytes.Geometry / 1024} + {t.Bytes.Draws / 1024} + {t.Bytes.Materials / 1024} KiB), {t.Bytes.Pages / (1 << 20)} MiB of pages"
		+ (GpuDrivenLeftClassic.IsEmpty ? "" : "; aggregate batches left classic (batch draws while timed, shader, mode, why): " + string.Join( ", ", GpuDrivenLeftClassic.OrderByDescending( x => x.Value ).Select( x => $"{x.Value} {x.Key}" ) ));

	/// <summary>While <see cref="TimeDraws"/>, aggregate batches the GPU-driven path left to the classic one, by shader, mode and why.</summary>
	internal readonly System.Collections.Concurrent.ConcurrentDictionary<string, int> GpuDrivenLeftClassic = new();

	/// <summary>The vertex layout GPU-driven pipelines are made with: none, their vertex shaders pull their inputs.</summary>
	internal VertexLayout PulledLayout => pulledLayout ??= Layout( new VertexLayout( 4, [] ) );

	/// <summary>The same for pipelines whose pixel shader reads its bucket's one material (a layout of its own keeps their material objects apart).</summary>
	internal VertexLayout PulledLayoutMaterialPerDraw => pulledLayoutPerDraw ??= Layout( new VertexLayout( 8, [] ) );

	VertexLayout pulledLayout, pulledLayoutPerDraw;

	/// <summary>
	/// Split GPU-driven buckets whose pixel shader reads its material (forward, alpha-tested depth) by material, so the
	/// pixel shader reads the record at an offset the compiler knows is uniform (specialization constant 120) rather than
	/// one carried per pixel: more multi-draws, no binds between them, and the classic shader's registers. Off by default:
	/// one multi-draw for every material of a pipeline costs complex 1 to 13 registers but draws Construct faster on CPU
	/// and GPU; on is for a shader whose registers matter more than its draws.
	/// </summary>
	public bool GpuDrivenMaterialBuckets { get; set; }

	readonly System.Collections.Concurrent.ConcurrentDictionary<uint, BackendParameters> gpuDrivenMaterialParameters = new();
	static readonly StringToken GpuDrivenMaterialName = new( "BasaltGpuDrivenMaterial_t" );

	/// <summary>
	/// The object attributes a bucket of one material draws with: <c>BasaltGpuDrivenMaterial_t</c> holding its pixel
	/// material record, made once per record, so buckets of other materials differ only in a dynamic offset.
	/// </summary>
	internal BackendParameters GpuDrivenMaterialParameters( uint pixelMaterial ) => gpuDrivenMaterialParameters.GetOrAdd( pixelMaterial, static record =>
	{
		var parameters = new BackendParameters();
		parameters.SetData( GpuDrivenMaterialName, new System.Numerics.Vector4( BitConverter.UInt32BitsToSingle( record ), 0, 0, 0 ) );
		return parameters;
	} );

	/// <summary>Time every model draw's binding and recording into <see cref="DrawTimes"/>, for benchmarks.</summary>
	public bool TimeDraws { get; set; }

	/// <summary>Model draws timed with <see cref="TimeDraws"/> on.</summary>
	public DrawTimings DrawTimes { get; } = new();

	/// <summary>The unit box the 2D sky draws (native's well-known box shape, half a unit each way), made once.</summary>
	internal RenderMesh SkyBox => skyBox ??= MakeSkyBox();

	RenderMesh skyBox;

	RenderMesh MakeSkyBox()
	{
		var vertices = new MeshVertex[8];
		for ( int i = 0; i < 8; i++ ) vertices[i] = new MeshVertex( new Vector3( (i & 1) != 0 ? 0.5f : -0.5f, (i & 2) != 0 ? 0.5f : -0.5f, (i & 4) != 0 ? 0.5f : -0.5f ), Vector3.Up, default );

		// Two triangles a face; the sky draws with culling off, so the winding doesn't matter
		uint[] indices = [0, 1, 3, 0, 3, 2, 4, 6, 7, 4, 7, 5, 0, 4, 5, 0, 5, 1, 2, 3, 7, 2, 7, 6, 0, 2, 6, 0, 6, 4, 1, 5, 7, 1, 7, 3];
		return BackendMesh.Create( this, vertices, indices, null, "Sky box" );
	}

	/// <summary>
	/// When set, the next frame context to begin a list of its own resets timestamps <c>Index</c> and <c>Index + 1</c> of
	/// <c>Queries</c> and writes the first at the start of its list, so a frame's GPU time can be read from there to a
	/// timestamp the caller writes after it; cleared once used. For benchmarks.
	/// </summary>
	internal (TimestampQueries Queries, int Index)? FrameStartTimestamp { get; set; }

	/// <summary>
	/// When set, every GPU scope Basalt records (its layers and setup) and every list a context submits is timed, and
	/// counted with <see cref="PassTimer.Statistics"/>. For benchmarks.
	/// </summary>
	internal PassTimer PassTimer { get; set; }

	/// <summary>
	/// Submit a frame's lists together, in one submission (<see cref="Device.SubmitLater"/>), rather than one submission
	/// each: on an RX 7900 XTX each submission drains the GPU, which cost Construct's frame recorded on workers (40
	/// lists) 0.66 ms of GPU time. Sent on when the render system has submitted the frame (<see cref="EndSubmitting"/>).
	/// </summary>
	internal bool BatchSubmits { get; set; } = true;

	/// <summary>Send the frame's lists held for one submission (<see cref="BatchSubmits"/>) to the GPU.</summary>
	public override void EndSubmitting() => Gpu.Flush();

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
		CompileGpuDrivenWithShaderCompiler();
		Defaults = new ShaderDefaults( gpu );

		// Bindless buffers shaders may read: nothing animated, no pipeline texture indices
		gpu.SetBindlessBuffer( ShaderAbi.BindlessVertexCache, Defaults.Storage );
		gpu.SetBindlessBuffer( ShaderAbi.BindlessPipelineTextures, Defaults.Storage );

		black = Placeholder( "Black", cube: false );
		sheetMetadata = Placeholder( "Sheet metadata", cube: false );

		// GPU-driven by default: the pages before any model loads, which the classic path draws from too
		if ( GpuDrivenByDefault )
		{
			GpuDrivenGeometry = true;
			GpuDriven = true;
		}
	}

	/// <summary>
	/// Whether a new device draws GPU-driven (<see cref="GpuDriven"/> and <see cref="GpuDrivenGeometry"/>): on unless the
	/// process was started with <c>-no-gpudriven</c>, for comparisons.
	/// </summary>
	public static bool GpuDrivenByDefault { get; set; } = !HasSwitch( "-no-gpudriven" );

	static bool HasSwitch( string name ) => Environment.GetCommandLineArgs().Any( x => x.Equals( name, StringComparison.OrdinalIgnoreCase ) );

	/// <summary>
	/// Shaders without a GPU-driven variant beside them (a package's) get one compiled at load by the ShaderCompiler
	/// beside the application, from the source embedded in their compiled file, into <c>game/.source2/basaltgpudriven</c>;
	/// their draws stay classic until it's ready. One compiled by an older engine is recompiled the same way first, into
	/// <c>game/.source2/basaltshaderupgrade</c>. Nothing when there's no ShaderCompiler or game folder.
	/// </summary>
	void CompileGpuDrivenWithShaderCompiler()
	{
		var compiler = Path.Combine( AppContext.BaseDirectory, "ShaderCompiler.exe" );
		var core = Content.Roots.FirstOrDefault( x => File.Exists( Path.Combine( x, "shaders", "basalt_gpu_driven.fxc" ) ) );
		if ( !File.Exists( compiler ) || core is null ) return;

		var game = Path.GetDirectoryName( core );
		Shaders.GpuDrivenCache = Path.Combine( game, ".source2", "basaltgpudriven" );
		// The variant's form and the wrapper it appends, not the build: a rebuild that changes neither compiles nothing
		var wrapper = Convert.ToHexString( System.Security.Cryptography.SHA256.HashData( File.ReadAllBytes( Path.Combine( core, "shaders", "basalt_gpu_driven.fxc" ) ) ), 0, 8 );
		Shaders.GpuDrivenCompilerVersion = $"{ShaderLibrary.GpuDrivenVersion}.{wrapper}";
		System.Threading.Tasks.Task.Run( Shaders.PruneGpuDrivenCache );
		Shaders.GpuDrivenCompiler = ( source, output ) =>
		{
			// Core shaders embed no source: build-shaders makes theirs, and one without stays classic
			if ( Path.GetFullPath( source ).StartsWith( core, StringComparison.OrdinalIgnoreCase ) ) return false;
			return RunShaderCompiler( compiler, game, ["--recompile", source, "-gpudriven", "-q", "--out", output] );
		};

		// A package shader an older engine compiled is recompiled from its embedded source as it loads, as the engine's
		// ShaderUpgrade does when a package installs
		Shaders.UpgradeCache = Path.Combine( game, ".source2", "basaltshaderupgrade" );
		Shaders.UpgraderVersion = $"{File.GetLastWriteTimeUtc( compiler ).Ticks}";
		Shaders.Upgrader = ( source, output ) => RunShaderCompiler( compiler, game, ["--recompile", source, "-q", "--out", output] );
	}

	static bool RunShaderCompiler( string compiler, string workingDirectory, string[] arguments )
	{
		var info = new ProcessStartInfo( compiler ) { WorkingDirectory = workingDirectory, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
		foreach ( var argument in arguments ) info.ArgumentList.Add( argument );

		using var process = Process.Start( info );
		if ( process is null ) return false;
		process.OutputDataReceived += static ( _, _ ) => { };
		process.ErrorDataReceived += static ( _, _ ) => { };
		process.BeginOutputReadLine();
		process.BeginErrorReadLine();
		if ( process.WaitForExit( TimeSpan.FromMinutes( 10 ) ) ) return process.ExitCode == 0;

		process.Kill( true );
		return false;
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
		// Multisampled targets aren't sampled, except depth: a frame's depth chain reads it (D_MSAA), as Source 2's targets allow
		if ( samples == 1 || format.IsDepth() ) usage |= TextureUsage.Sampled;
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
		// From the content's folders or packages (a map's VPK)
		var data = Content.TryRead( path );
		if ( data is null ) return null;

		var texture = TextureFile.Read( data );
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
			return sharedMaterials[path] = Converted( path ) ?? Material( path, null );
		}
	}

	/// <summary>
	/// A material whose <c>.shader</c> has a surface standing in for it (<see cref="SurfaceShaders.Convert"/>), as that
	/// surface with the material's values; null when it has none or doesn't compile.
	/// </summary>
	GpuMaterial Converted( string path )
	{
		if ( surfaces is not { HasConversions: true } || Content.TryRead( path ) is not { } data || ReadMaterial( data ) is not { } file ) return null;
		if ( surfaces.ConversionFor( file.Shader ) is not { } surface || surfaces.Compile( surface ) is not { } compiled ) return null;
		surfaces.CountConverted();
		return ConvertedMaterial.Create( compiled, file, path, texture => LoadTexture( texture ) );
	}

	/// <summary>A material that's a shader with its defaults.</summary>
	public override GpuMaterial MaterialFromShader( string shaderPath )
	{
		lock ( sharedMaterials )
		{
			if ( sharedMaterials.TryGetValue( shaderPath, out var material ) ) return material;
			return sharedMaterials[shaderPath] = Track( new BackendMaterial( shaderPath, Shaders.Load( shaderPath ), null, null ) );
		}
	}

	/// <inheritdoc/>
	public override GpuMaterial CreateMaterial( string name, string shaderPath ) => Track( new BackendMaterial( name, Shaders.Load( shaderPath ), null, null ) );

	/// <summary>
	/// A lit material of one colour, for meshes made with <see cref="BackendMesh.Create"/>:
	/// <c>materials/default/white.vmat</c> (complex.shader, white, flat normal) tinted with <paramref name="albedo"/>.
	/// </summary>
	public GpuMaterial CreateMaterial( string name, Color albedo )
	{
		var values = new Dictionary<string, float[]>( StringComparer.OrdinalIgnoreCase ) { ["g_vColorTint"] = [albedo.r, albedo.g, albedo.b, 0] };
		return Content.Find( "materials/default/white.vmat" ) is null
			? Track( new BackendMaterial( name, Shaders.Load( "shaders/complex.shader" ), null, null, values ) )
			: Material( "materials/default/white.vmat", values, name );
	}

	/// <summary>
	/// A compiled material with <paramref name="values"/> (shader variables by name) over its own. It draws nothing if
	/// it, or its shader, isn't built.
	/// </summary>
	public GpuMaterial LoadMaterial( string path, IReadOnlyDictionary<string, float[]> values, string name = null )
		=> Material( path, values is null ? null : new( values, StringComparer.OrdinalIgnoreCase ), name );

	/// <summary>A material file, or null when it can't be read (KV3 with binary blobs isn't yet), so it draws nothing.</summary>
	static MaterialFile ReadMaterial( byte[] data )
	{
		try
		{
			return MaterialFile.Read( data );
		}
		catch ( System.IO.InvalidDataException )
		{
			return null;
		}
	}

	BackendMaterial Material( string path, Dictionary<string, float[]> values, string name = null )
	{
		var file = Content.TryRead( path ) is { } data ? ReadMaterial( data ) : null;
		if ( file is null ) return Track( new BackendMaterial( name ?? path, null, null, null, values ) );
		return Track( new BackendMaterial( name ?? path, Shaders.Load( file.Shader ), file, Textures( file ), values ) );
	}

	/// <summary>
	/// Compiled models: a <c>.vmdl_c</c> from the content as a mesh Basalt draws, with its LODs and materials, or null
	/// when it isn't built. See <see cref="BackendModel.Load"/>.
	/// </summary>
	public RenderMesh LoadModel( string path, ulong? meshGroups = null, int materialGroup = 0 ) => BackendModel.Load( this, path, meshGroups, materialGroup );

	/// <summary>The vertex layouts pipelines have been made for, for tests.</summary>
	internal VertexLayout[] PipelineLayouts
	{
		get
		{
			lock ( pipelines ) return [.. pipelines.Keys.Select( k => k.Layout ).Distinct()];
		}
	}

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

	/// <summary>The colour an ids pipeline is keyed by (<see cref="Pipeline"/>'s ids), which no other pipeline draws into.</summary>
	internal const Format IdsColorKey = Format.R32Uint;

	/// <summary>
	/// A pipeline for a variant drawing vertices laid out as <paramref name="layout"/> into targets of these formats,
	/// made once. Mesh vertices are stream 0, instance ids stream 1 (<c>TEXCOORD13</c>), and inputs the mesh lacks, or
	/// has as integers where the shader wants floats (or the other way round), read the default stream 2, as native's
	/// input layouts do for streams a mesh doesn't have. A vertex-cache layout reads vertex ids from streams 2 and 3 and
	/// the default stream from 4.
	/// </summary>
	internal PipelineHandle Pipeline( ShaderVariant variant, VertexLayout layout, Format color, Format depth, int samples, bool depthOnly, BindingLayoutHandle[] sets = null, bool materialPerDraw = false, bool ids = false, ShaderVariant stateFrom = null )
	{
		var key = new PipelineKey( variant, layout, ids ? IdsColorKey : color, depth, samples, depthOnly, sets, materialPerDraw, stateFrom );
		lock ( pipelines )
		{
			if ( pipelines.TryGetValue( key, out var pipeline ) ) return pipeline;

			// A vertex-cache draw reads its vertex ids from streams 2 and 3, as floats
			var vertexCache = layout.VertexCache;
			var attributes = ShaderDefaults.MapVertices( variant.VertexInputs, input =>
			{
				if ( input.Semantic == VertexSemantic.TexCoord && input.SemanticIndex == 13 ) return new VertexAttribute( 0, 1, VertexFormat.UInt, 0 );
				if ( vertexCache && input.Semantic == VertexSemantic.TexCoord && input.SemanticIndex is 14 or 15 ) return new VertexAttribute( 0, input.SemanticIndex == 14 ? 2 : 3, VertexFormat.Float, 0 );
				if ( layout.Find( input.Semantic, input.SemanticIndex ) is not { } field ) return null;
				var integer = field.Format is VertexFormat.UByte4 or VertexFormat.UInt or VertexFormat.UInt2 or VertexFormat.UInt4;
				if ( integer != (input.Kind != SpirvScalar.Float) ) return null;
				return new VertexAttribute( 0, 0, field.Format, field.Offset );
			}, defaultBuffer: vertexCache ? 4 : 2 );

			VertexBufferLayout[] streams = vertexCache
				? [new( layout.Stride ), new( 4, VertexStepRate.Instance ), new( 4 ), new( 4 ), new( 0 )]
				: [new( layout.Stride ), new( 4, VertexStepRate.Instance ), new( 0 )];
			// The visibility-driven forward pass's id prepass: target 0 takes the authored output (its alpha drives alpha to
			// coverage) and goes nowhere, target 1 the ids (a GPU-driven pixel shader's id output)
			Format[] colors = ids ? [Format.R8Unorm, Format.R32Uint] : color == Format.Unknown ? [] : [color];
			var info = variant.PipelineInfo( streams, attributes, colors, depth, samples, depthBias: depthOnly || color == Format.Unknown || ids, $"{variant}{(depthOnly ? " depth" : ids ? " ids" : "")}" );
			if ( ids ) info.Blend = [BlendState.Opaque, BlendState.Opaque];
			if ( sets is not null ) info.Sets = sets;

			// basalt_gpu_driven_ids drawing another variant's depth: that variant's culling, depth and stencil
			if ( stateFrom?.State is { } state )
			{
				info.CullMode = state.Cull;
				info.DepthClamp = !state.DepthClip;
				info.DepthTest = depth != Format.Unknown && state.DepthTest;
				info.DepthWrite = depth != Format.Unknown && state.DepthWrite;
				info.DepthCompare = state.DepthCompare;
				info.StencilTest = depth.HasStencil() && state.StencilTest;
				info.StencilFront = state.StencilFront;
				info.StencilBack = state.StencilBack;
				info.StencilReadMask = state.StencilReadMask;
				info.StencilWriteMask = state.StencilWriteMask;
			}

			// A GPU-driven pixel shader reading its bucket's one material (basalt_gpu_driven.h's specialization constant)
			if ( materialPerDraw && info.FragmentShader.IsValid ) info.FragmentConstants = [.. info.FragmentConstants ?? [], new SpecializationConstant( GpuDrivenMaterialPerDrawConstant, 1 )];

			// Stripped pixel shaders: depth passes of opaque materials don't need them (alpha-tested ones keep theirs)
			if ( depthOnly && color == Format.Unknown )
			{
				info.FragmentShader = default;
				info.FragmentEntry = null;
				info.FragmentConstants = null;
			}

			pipeline = Gpu.CreateGraphicsPipeline( info );
			pipelines[key] = pipeline;
			if ( DumpPixelShaders is { } dump && info.FragmentShader.IsValid ) DumpPixelShader( dump, variant, info.FragmentConstants, materialPerDraw );
			return pipeline;
		}
	}

	/// <summary>
	/// <c>BASALT_DUMP_PIXEL_SHADERS=folder</c>: write every pixel shader a pipeline is made with, specialization constants
	/// applied, as <c>shader_mode_static_dynamic_kind.spv</c> (kind: classic, gpudriven, or gpudriven-perdraw), so register
	/// counts of the classic and GPU-driven forms of one combo can be compared offline (amdllpc).
	/// </summary>
	static readonly string DumpPixelShaders = Environment.GetEnvironmentVariable( "BASALT_DUMP_PIXEL_SHADERS" );

	static void DumpPixelShader( string folder, ShaderVariant variant, SpecializationConstant[] constants, bool materialPerDraw )
	{
		var stage = variant.Pixel;
		var kind = !stage.GpuDriven ? "classic" : materialPerDraw ? "gpudriven-perdraw" : "gpudriven";
		var name = $"{System.IO.Path.GetFileName( variant.Shader.Name ).Split( '.' )[0]}_{variant.Mode?.Name}_s{stage.Static.Id}_d{stage.Dynamic.Id}_{kind}.spv";
		var path = System.IO.Path.Combine( folder, name );
		if ( System.IO.File.Exists( path ) ) return;

		var spirv = (byte[])stage.Bytecode.Shared.Spirv.Clone();
		var words = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, uint>( spirv.AsSpan() );
		var specIds = new Dictionary<uint, uint>();
		for ( int pass = 0; pass < 2; pass++ )
		{
			for ( int i = 5; i < words.Length; )
			{
				var count = (int)(words[i] >> 16);
				var opcode = (int)(words[i] & 0xffff);
				if ( count == 0 ) return;
				if ( pass == 0 && opcode == 71 && count >= 4 && words[i + 2] == 1 ) specIds[words[i + 1]] = words[i + 3];
				else if ( pass == 1 && opcode is 48 or 49 or 50 && specIds.TryGetValue( words[i + 2], out var id ) )
				{
					foreach ( var c in constants ?? [] )
					{
						if ( c.Id != id ) continue;
						if ( opcode == 50 ) words[i + 3] = c.Value;
						else words[i] = (words[i] & 0xffff0000) | (uint)(c.Value != 0 ? 48 : 49);
					}
				}

				i += count;
			}
		}

		System.IO.Directory.CreateDirectory( folder );
		System.IO.File.WriteAllBytes( path, spirv );
	}

	internal readonly record struct PipelineKey( ShaderVariant Variant, VertexLayout Layout, Format Color, Format Depth, int Samples, bool DepthOnly, BindingLayoutHandle[] Sets = null, bool MaterialPerDraw = false, ShaderVariant StateFrom = null );

	/// <summary><c>BASALT_GPU_DRIVEN_MATERIAL_PER_DRAW_ID</c>: set, a GPU-driven pixel shader reads <c>g_nBasaltPixelMaterial</c>.</summary>
	const uint GpuDrivenMaterialPerDrawConstant = 120;

	/// <summary>A compute pipeline for a compute shader's variant (with <paramref name="sets"/> for its layout's), made once.</summary>
	internal PipelineHandle ComputePipeline( ShaderVariant variant, BindingLayoutHandle[] sets = null )
	{
		var key = new PipelineKey( variant, null, Format.Unknown, Format.Unknown, 0, false, sets );
		lock ( pipelines )
		{
			if ( pipelines.TryGetValue( key, out var pipeline ) ) return pipeline;
			pipeline = Gpu.CreateComputePipeline( new ComputePipelineInfo
			{
				Shader = variant.Compute.Module,
				Entry = variant.Compute.Bytecode.EntryPoint( ShaderProgramType.Compute ),
				Constants = variant.Compute.Constants,
				Sets = sets ?? variant.Sets,
				Name = variant.ToString(),
			} );
			pipelines[key] = pipeline;
			return pipeline;
		}
	}

	internal override GpuContext CreateContext( GpuContext frame = null ) => new BackendContext( this, (BackendContext)frame );

	internal override bool DrawsAggregatesGpuDriven => GpuDriven && MaterialObjects && GpuDrivenTables is not null;

	internal override bool DrawsMeshesGpuDriven => GpuDrivenMeshes && DrawsAggregatesGpuDriven;

	/// <summary>
	/// Static meshes' opaque draws join the aggregates' GPU-driven call (docs/managed/basalt-gpu-driven.md). Off by default:
	/// on Construct it saves GPU time but costs CPU time, mostly on worker threads, where the one GPU-driven run now records
	/// what workers drew between them; <c>-gpudriven-meshes</c> turns it on.
	/// </summary>
	public bool GpuDrivenMeshes { get; set; } = HasSwitch( "-gpudriven-meshes" );

	/// <summary>
	/// Cull GPU-driven aggregates' fragments on the GPU (<c>basalt_gpu_driven_cull_cs</c>), by the same frustum and size
	/// tests the CPU makes, from records written once: the CPU writes 16 bytes per aggregate a frame instead of an instance
	/// record and command per visible fragment. On by default; off writes them on the CPU, to compare.
	/// </summary>
	public bool GpuDrivenCulling { get; set; } = true;

	/// <summary>
	/// With <see cref="GpuDrivenCulling"/>, the culling writes only the kept fragments' commands, packed at the start of
	/// each multi-draw, which draws as many as it kept (<c>DrawIndexedIndirectCount</c>). On by default where the device
	/// takes a draw count from a buffer; off leaves a command per fragment with no instance where culled, to compare.
	/// </summary>
	public bool GpuDrivenCompaction { get; set; } = true;

	/// <summary>The culling compute shader, or null when it isn't built.</summary>
	internal ShaderFile GpuDrivenCullShader
	{
		get
		{
			if ( !gpuDrivenCullShaderLoaded )
			{
				gpuDrivenCullShader = Shaders.Load( "shaders/basalt_gpu_driven_cull_cs.shader" );
				gpuDrivenCullShaderLoaded = true;
			}

			return gpuDrivenCullShader;
		}
	}

	ShaderFile gpuDrivenCullShader;
	bool gpuDrivenCullShaderLoaded;

	/// <summary>
	/// The visibility-driven forward pass (docs/managed/basalt-gpu-driven.md): with <see cref="GpuDrivenCulling"/> and
	/// compaction, a view's GPU-driven depth prepass also writes ids, a pass marks the triangles that own a sample, and its
	/// forward pass draws only those. On by default (it only acts where <see cref="GpuDriven"/> does); off with
	/// <c>-no-gpudriven-visibility</c>, for comparisons.
	/// </summary>
	public bool GpuDrivenVisibility { get; set; } = !HasSwitch( "-no-gpudriven-visibility" );

	/// <summary>The frame's stamp for <see cref="GpuDrivenVisibility"/>: fragments the prepass drew ids for this frame carry it.</summary>
	internal uint GpuDrivenFrame { get; private set; } = 1;

	/// <summary>The id prepass material for depth buckets without a pixel shader, the mark and compaction shaders, or null when they aren't built.</summary>
	internal (BackendMaterial Ids, ShaderFile Mark, ShaderFile Compact) GpuDrivenVisibilityShaders
	{
		get
		{
			lock ( visibilityViews )
			{
				if ( !visibilityShadersLoaded )
				{
					visibilityShadersLoaded = true;
					if ( Shaders.Load( "shaders/basalt_gpu_driven_ids.shader" ) is not null ) visibilityIds = MaterialFromShader( "shaders/basalt_gpu_driven_ids.shader" ) as BackendMaterial;
					visibilityMark = Shaders.Load( "shaders/basalt_gpu_driven_mark_cs.shader" );
					visibilityCompact = Shaders.Load( "shaders/basalt_gpu_driven_compact_cs.shader" );
				}

				return (visibilityIds, visibilityMark, visibilityCompact);
			}
		}
	}

	BackendMaterial visibilityIds;
	ShaderFile visibilityMark, visibilityCompact;
	bool visibilityShadersLoaded;

	/// <summary>
	/// Debug only (<c>basaltlab -capture-visibility</c>): GPU-driven forward passes also replay their multi-draws into this
	/// figure's targets. Set it before the first frame, so the view's ids and mask can be copied out too.
	/// </summary>
	public VisibilityFigure Figure { get; set; }

	/// <summary>
	/// The figure's ids target and mask (the last GPU-driven forward pass's view's, for <see cref="Figure"/>), the bits
	/// aggregates use from 0 and those the frame's mesh draws use from <see cref="FigureFrameBitBase"/>.
	/// </summary>
	public (TextureHandle Ids, BufferHandle Mask, long AggregateBits, long MeshBits) FigureVisibility { get; internal set; }

	/// <summary>Where the frame's mesh draws' vertex bits start in the mask.</summary>
	public const long FigureFrameBitBase = GpuDrivenTables.FrameVisibilityBitBase;

	internal BackendMaterial FigureMaterial => figureMaterial ??= Shaders.Load( "shaders/basalt_gpu_driven_figure.shader" ) is not null ? MaterialFromShader( "shaders/basalt_gpu_driven_figure.shader" ) as BackendMaterial : null;
	BackendMaterial figureMaterial;

	readonly Dictionary<(ShaderVariant StateFrom, int Mode, Format Depth), PipelineHandle> figurePipelines = new();

	/// <summary>
	/// The figure's pipeline for <paramref name="mode"/> (0 count, 1 flat triangles, 2 dark edges, 3 faint edges), culling
	/// as <paramref name="stateFrom"/> does: counts and faint edges add up with no depth test, flat triangles and dark edges
	/// test the view's depth without writing it.
	/// </summary>
	internal PipelineHandle FigurePipeline( ShaderVariant variant, ShaderVariant stateFrom, int mode, Format depth, BindingLayoutHandle[] sets )
	{
		lock ( figurePipelines )
		{
			if ( figurePipelines.TryGetValue( (stateFrom, mode, depth), out var made ) ) return made;

			Format[] colors = [mode == 0 ? Format.R16Float : Format.RGBA8Unorm];
			var info = variant.PipelineInfo( [], [], colors, depth, 1, depthBias: false, $"{variant} figure {mode}" );
			info.Sets = sets;
			info.CullMode = stateFrom.State.Cull;
			info.Wireframe = mode >= 2;
			info.DepthTest = mode is 1 or 2;
			info.DepthWrite = false;
			info.DepthCompare = CompareFunction.GreaterEqual;
			info.StencilTest = false;
			info.Blend = [mode is 0 or 3 ? BlendState.Additive : BlendState.Opaque];
			return figurePipelines[(stateFrom, mode, depth)] = Gpu.CreateGraphicsPipeline( info );
		}
	}

	/// <summary>A view's visibility resources: its ids target, triangle mask, fragment stamps and compacted indices.</summary>
	internal sealed class VisibilityView
	{
		public TextureHandle Ids, Scratch;
		public int Width, Height, Samples;
		public BufferHandle Mask, Stamps;
		public readonly BufferHandle[] Compacted = new BufferHandle[3];
		public bool StampsCleared;
	}

	/// <summary>Indices each compacted index buffer holds; a fragment that doesn't fit draws whole.</summary>
	internal const int VisibilityCompactedIndices = 4 << 20;

	readonly ConditionalWeakTable<object, VisibilityView> visibilityViews = new();
	readonly List<BufferHandle> visibilityBuffers = new();
	readonly List<TextureHandle> visibilityTextures = new();

	/// <summary>A view's visibility resources, made the first time and remade when its depth target changes size.</summary>
	internal VisibilityView VisibilityFor( object view, TextureInfo depth )
	{
		lock ( visibilityViews )
		{
			var state = visibilityViews.GetValue( view, static _ => new VisibilityView() );
			if ( !state.Mask.IsValid )
			{
				// Copied out only for the debug figure
				var figure = Figure is not null;
				state.Mask = Gpu.CreateBuffer( new BufferInfo { Size = GpuDrivenTables.MaxVisibilityBits / 8, Usage = BufferUsage.Storage | BufferUsage.CopyDestination | (figure ? BufferUsage.CopySource : 0), Name = "Basalt visibility mask" } );
				state.Stamps = Gpu.CreateBuffer( new BufferInfo { Size = GpuDrivenTables.MaxVisibilityFragments * 4L, Usage = BufferUsage.Storage | BufferUsage.CopyDestination, Name = "Basalt visibility stamps" } );
				for ( int i = 0; i < state.Compacted.Length; i++ )
				{
					state.Compacted[i] = Gpu.CreateBuffer( new BufferInfo { Size = VisibilityCompactedIndices * 4L, Usage = BufferUsage.Index | BufferUsage.Storage, Name = "Basalt visibility triangles" } );
					visibilityBuffers.Add( state.Compacted[i] );
				}

				visibilityBuffers.Add( state.Mask );
				visibilityBuffers.Add( state.Stamps );
			}

			if ( depth.Width != 0 && (state.Width != depth.Width || state.Height != depth.Height || state.Samples != depth.Samples) )
			{
				foreach ( var old in (TextureHandle[])[state.Ids, state.Scratch] )
				{
					if ( !old.IsValid ) continue;
					visibilityTextures.Remove( old );
					Gpu.Destroy( old );
				}

				// Target 0 of the id prepass: the authored output, which only its alpha (to coverage) is wanted from
				state.Scratch = Gpu.CreateTexture( new TextureInfo { Width = depth.Width, Height = depth.Height, Depth = 1, Layers = 1, Mips = 1, Samples = depth.Samples, Dimension = TextureDimension.Texture2D, Format = Format.R8Unorm, Usage = TextureUsage.ColorTarget, Name = "Basalt visibility scratch" } );
				visibilityTextures.Add( state.Scratch );

				state.Ids = Gpu.CreateTexture( new TextureInfo
				{
					Width = depth.Width,
					Height = depth.Height,
					Depth = 1,
					Layers = 1,
					Mips = 1,
					Samples = depth.Samples,
					Dimension = TextureDimension.Texture2D,
					Format = Format.R32Uint,
					Usage = TextureUsage.ColorTarget | TextureUsage.Sampled | (Figure is not null ? TextureUsage.CopySource : 0),
					Name = "Basalt visibility ids",
				} );
				visibilityTextures.Add( state.Ids );
				(state.Width, state.Height, state.Samples) = (depth.Width, depth.Height, depth.Samples);
			}

			return state;
		}
	}

	/// <summary>Async compute isn't wired yet: contexts all record on the graphics queue.</summary>
	public override bool SupportsAsyncCompute => false;

	/// <inheritdoc/>
	public override bool SupportsDrawIndirectCount => Gpu.Capabilities.DrawIndirectCount;

	//
	// The vertex cache: skinned vertices compute skinning writes for the frame (CachedAnimatedVertex_t, 24 bytes each), which
	// vertex shaders read through the bindless table's vertex cache binding. One region per frame in flight, so a frame
	// writes while the one before is still read.
	//

	/// <summary>Bytes a cached vertex takes (<c>CachedAnimatedVertex_t</c>).</summary>
	internal const int VertexCacheStride = 24;

	readonly Lock vertexCacheLock = new();
	BufferHandle vertexCacheBuffer;
	int vertexCacheCapacity;
	ulong vertexCacheFrame = ulong.MaxValue;
	bool vertexCacheGrew;

	/// <summary>The vertex cache, or an invalid handle before anything was skinned.</summary>
	internal BufferHandle VertexCacheBuffer => vertexCacheBuffer;

	/// <summary>
	/// Room for <paramref name="vertices"/> skinned vertices in this frame's region of the vertex cache; the first one's
	/// index. A region that's too small grows the cache, after the GPU is idle, and the next frame waits for this one too,
	/// since this frame's earlier blocks may lie in the next one's region.
	/// </summary>
	public override int AllocateVertexCache( int vertices )
	{
		lock ( vertexCacheLock )
		{
			if ( Gpu.FrameNumber != vertexCacheFrame )
			{
				if ( vertexCacheGrew ) Gpu.WaitIdle();
				vertexCacheGrew = false;
				vertexCacheFrame = Gpu.FrameNumber;
				vertexCache = 0;
			}

			if ( vertexCache + vertices > vertexCacheCapacity ) GrowVertexCache( vertexCache + vertices );

			var first = Gpu.FrameSlot * vertexCacheCapacity + vertexCache;
			vertexCache += vertices;
			return first;
		}
	}

	void GrowVertexCache( int needed )
	{
		Gpu.WaitIdle();
		var capacity = Math.Max( 1 << 16, vertexCacheCapacity );
		while ( capacity < needed ) capacity *= 2;

		var buffer = Gpu.CreateBuffer( new BufferInfo { Size = (long)capacity * VertexCacheStride * Gpu.FramesInFlight, Usage = BufferUsage.Storage, Name = "Basalt vertex cache" } );
		Gpu.SetBindlessBuffer( ShaderAbi.BindlessVertexCache, buffer );
		if ( vertexCacheBuffer.IsValid ) Gpu.Destroy( vertexCacheBuffer );
		vertexCacheBuffer = buffer;
		vertexCacheCapacity = capacity;
		vertexCacheGrew = true;
	}

	/// <summary>Vertices <see cref="VertexIds"/> numbers (native's <c>VERTEX_ID_BUFFER_MAX_VERTICES</c>).</summary>
	internal const int MaxVertexIds = 131072;

	BufferHandle vertexIds;

	/// <summary>
	/// 0, 1, 2 ... as floats, one per vertex (native's <c>SCENESYSTEMVB_INSTANCE_VERTEX_ID</c>): bound at an offset, it gives
	/// a vertex-cache draw's vertices their place in the cache.
	/// </summary>
	internal BufferHandle VertexIds
	{
		get
		{
			lock ( vertexCacheLock )
			{
				if ( vertexIds.IsValid ) return vertexIds;
				var ids = new float[MaxVertexIds];
				for ( int i = 0; i < ids.Length; i++ ) ids[i] = i;
				var buffer = Gpu.CreateBuffer( new BufferInfo { Size = ids.Length * sizeof( float ), Usage = BufferUsage.Vertex, Name = "Basalt vertex ids" } );
				Gpu.WriteBuffer( buffer, System.Runtime.InteropServices.MemoryMarshal.AsBytes( ids.AsSpan() ) );
				return vertexIds = buffer;
			}
		}
	}

	/// <summary>The frame's composite morph atlas, which <see cref="BackendMorphs"/> queue meshes in.</summary>
	internal MorphAtlas Morphs => morphs ??= new MorphAtlas( this );

	MorphAtlas morphs;

	/// <inheritdoc/>
	public override Vector4 MorphSubrect( int handle ) => Morphs.Subrect( handle );

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
		gpuDrivenTables?.BeginFrame();
		GpuDrivenFrame++;
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
		materialCache?.Dispose();
		surfaces?.Dispose();
		gpuDrivenTables?.Dispose();
		lock ( visibilityViews )
		{
			foreach ( var buffer in visibilityBuffers ) Gpu.Destroy( buffer );
			foreach ( var texture in visibilityTextures ) Gpu.Destroy( texture );
			visibilityBuffers.Clear();
			visibilityTextures.Clear();
		}

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

	/// <summary>
	/// GPU occlusion culling (off by default): the culler's compute pass, and culled draws from its arguments and surviving
	/// instance ids.
	/// </summary>
	public bool Occlusion { get; set; }

	/// <inheritdoc/>
	public override bool OcclusionCulling => Occlusion;
}
