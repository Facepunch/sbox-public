using Borealis.Gpu;
using Borealis.Source2.Shaders;
using Sandbox.ShaderCompiler;
using System.IO;
using System.Linq;

namespace Borealis.Backend;

/// <summary>
/// New-tier shaders on a <see cref="BackendDevice"/>: compiled from Slang by file name (searched in
/// <see cref="Folders"/>) the first time a <see cref="SurfaceMaterial"/> of theirs draws, with their modules, binding
/// layouts and pipelines, and the material records they read. Only Borealis.Gpu compiles them; old shaders are untouched.
/// </summary>
public sealed class SurfaceShaders : IDisposable
{
	readonly BackendDevice device;
	readonly Dictionary<string, SurfaceProgram> programs = new( StringComparer.OrdinalIgnoreCase );
	readonly List<string> folders = new();
	SlangCompiler compiler;

	internal SurfaceShaders( BackendDevice device )
	{
		this.device = device;
		Records = new SurfaceRecords( device.Gpu );
	}

	/// <summary>Folders searched for shaders by file name, in order.</summary>
	public IReadOnlyList<string> Folders => folders;

	/// <summary>Search <paramref name="folder"/> for shaders too.</summary>
	public void AddFolder( string folder )
	{
		folder = Path.GetFullPath( folder );
		lock ( programs ) folders.Add( folder );
		Watch( folder );
	}

	/// <summary>
	/// Draw <c>.vmat</c> materials of the <c>.shader</c> <paramref name="shader"/> (<c>shaders/blendable.shader</c>) with the
	/// surface in <paramref name="file"/> (<c>blendable.slang</c>) instead, from materials loaded after this. Each field
	/// takes the material's value of its <c>[Source]</c>, or of its name with a vfx prefix (<c>g_tColorA</c> for
	/// <c>ColorA</c>); the shader's defaults where the material sets none.
	/// </summary>
	public void Convert( string shader, string file )
	{
		lock ( programs ) conversions[ShaderKey( shader )] = file;
	}

	/// <summary>The surface standing in for <paramref name="shader"/>, or null.</summary>
	internal string ConversionFor( string shader )
	{
		if ( shader is null ) return null;
		var key = ShaderKey( shader );
		lock ( programs )
		{
			if ( conversions.TryGetValue( key, out var file ) ) return file;
		}

		return ConvertShaders && DefaultSurfaces.TryGetValue( key, out var surface ) ? DefaultSurface( key, surface ) : null;
	}

	/// <summary>
	/// The core shaders drawn as surfaces by default (<see cref="ConvertShaders"/>): by the surface written beside them
	/// (<c>complex.slang</c>), or, for null, by what <see cref="ShaderConverter"/> makes of the <c>.shader</c>.
	/// </summary>
	static readonly Dictionary<string, string> DefaultSurfaces = new( StringComparer.OrdinalIgnoreCase )
	{
		["complex"] = "complex.slang",
		["blendable"] = "blendable.slang",
		["generic"] = "generic.slang",
		["vertex_color"] = "vertex_color.slang",
		["static_overlay"] = "static_overlay.slang",
		["glass"] = "glass.slang",
		["foliage"] = "foliage.slang",
		["fur"] = "fur.slang",
		["simple"] = null,
		["bark"] = null,
		["terrain_preview"] = null,
		["water_simple"] = null,
	};

	/// <summary>
	/// The single switch between the surface tier and the <c>.shader</c> route for the core shaders that have a surface:
	/// on (the default), their materials load as surfaces; off, as their <c>.shader</c>, for comparisons. Read by each
	/// device as it's made (<see cref="ConvertShaders"/>).
	/// </summary>
	public static bool ConvertByDefault { get; set; } = true;

	/// <summary>
	/// Whether this device loads the core shaders' materials as the surfaces standing in for them; from
	/// <see cref="ConvertByDefault"/>. Shaders given with <see cref="Convert"/> convert either way.
	/// </summary>
	public bool ConvertShaders { get; set; } = ConvertByDefault;

	readonly Dictionary<string, string> defaultPaths = new( StringComparer.OrdinalIgnoreCase );

	/// <summary>A default surface's path: beside its .shader in the core shaders, or the converter's output in the cache.</summary>
	string DefaultSurface( string key, string surface )
	{
		lock ( defaultPaths )
		{
			if ( defaultPaths.TryGetValue( key, out var known ) ) return known;
			string path = null;
			try
			{
				var shaders = CoreShaders();
				if ( surface is not null ) path = File.Exists( Path.Combine( shaders, surface ) ) ? Path.Combine( shaders, surface ) : null;
				else if ( File.Exists( Path.Combine( shaders, key + ".shader" ) ) )
				{
					var result = ShaderConverter.Convert( File.ReadAllText( Path.Combine( shaders, key + ".shader" ) ), key, include => File.Exists( Path.Combine( shaders, include ) ) ? File.ReadAllText( Path.Combine( shaders, include ) ) : null );
					if ( result.Source is null ) Error?.Invoke( $"{key}.shader: not converted ({string.Join( "; ", result.Notes )})" );
					else
					{
						// Written to the cache, only when it changed, so its compile is cached too
						var folder = Path.Combine( Compiler().CacheFolder, "converted" );
						Directory.CreateDirectory( folder );
						path = Path.Combine( folder, key + ".slang" );
						var text = result.Source.Replace( "\n", "\r\n" );
						if ( !File.Exists( path ) || File.ReadAllText( path ) != text ) File.WriteAllText( path, text );
					}
				}
			}
			catch ( InvalidOperationException e )
			{
				Error?.Invoke( $"{key}.shader: {e.Message}" );
			}

			return defaultPaths[key] = path;
		}
	}

	/// <summary>
	/// A material of the surface in <paramref name="file"/> filled the way a converted <c>.vmat</c> is (see
	/// <see cref="Convert"/>): <paramref name="textures"/> (compiled texture paths) and <paramref name="values"/> by the
	/// <c>.shader</c>'s variable names. Null when the surface doesn't compile.
	/// </summary>
	public GpuMaterial CreateMaterial( string file, IReadOnlyDictionary<string, string> textures, IReadOnlyDictionary<string, float[]> values, string name = null )
	{
		if ( Compile( file ) is not { } compiled ) return null;
		var material = new Borealis.Source2.Materials.MaterialFile
		{
			Name = name ?? file,
			Shader = file,
			Ints = new Dictionary<string, int>(),
			Floats = new Dictionary<string, float>(),
			Vectors = values?.ToDictionary( v => v.Key, v => v.Value ) ?? new Dictionary<string, float[]>(),
			Textures = textures?.ToDictionary( t => t.Key, t => t.Value ) ?? new Dictionary<string, string>(),
			IntAttributes = new Dictionary<string, int>(),
			ShaderVersion = 1,
		};

		return ConvertedMaterial.Create( compiled, material, name ?? file, texture => device.LoadTexture( texture ) );
	}

	/// <summary>Whether any shader has a surface standing in for it.</summary>
	internal bool HasConversions
	{
		get
		{
			if ( ConvertShaders ) return true;
			lock ( programs ) return conversions.Count > 0;
		}
	}

	/// <summary>Materials loaded as the surface standing in for their shader.</summary>
	public int ConvertedMaterials => convertedMaterials;

	int convertedMaterials;
	readonly List<string> convertedPaths = new();

	/// <summary>Which materials of a converted shader load as the surface, by path; null for all. For comparisons.</summary>
	public Func<string, bool> ConvertMaterial { get; set; }

	/// <summary>The paths of the materials loaded as surfaces, in load order, for reports.</summary>
	public IReadOnlyList<string> ConvertedPaths
	{
		get
		{
			lock ( convertedPaths ) return convertedPaths.ToArray();
		}
	}

	internal void CountConverted( string path )
	{
		System.Threading.Interlocked.Increment( ref convertedMaterials );
		lock ( convertedPaths ) convertedPaths.Add( path );
	}

	/// <summary>Surface batches the GPU-driven path has taken, added up over every frame, for reports.</summary>
	public long GpuDrivenBatches => System.Threading.Interlocked.Read( ref gpuDrivenBatches );

	long gpuDrivenBatches;

	internal void CountGpuDriven() => System.Threading.Interlocked.Increment( ref gpuDrivenBatches );

	static string ShaderKey( string shader ) => Path.GetFileNameWithoutExtension( shader.Replace( '\\', '/' ) ).ToLowerInvariant();

	readonly Dictionary<string, string> conversions = new( StringComparer.OrdinalIgnoreCase );

	/// <summary>Called with a shader's compile errors; the material then draws nothing.</summary>
	public Action<string> Error { get; set; } = message => Console.Error.WriteLine( message );

	/// <summary>
	/// Whether the pixel stage reads a material's record at the offset the vertex stage passes on (on, the default: what
	/// one GPU-driven bucket per pipeline needs) or at the draw's own (off: uniform per draw, for buckets split by
	/// material). A specialization constant: both forms are compiled into every surface.
	/// </summary>
	public bool MaterialOffsetPerPixel { get; set; } = true;

	/// <summary>The records every surface material's fields are in.</summary>
	internal SurfaceRecords Records { get; }

	/// <summary>The engine's Slang and core shaders, beside the content's <c>core</c> folder.</summary>
	SlangCompiler Compiler()
	{
		if ( compiler is not null ) return compiler;
		var shaders = CoreShaders();
		var game = Path.GetDirectoryName( Path.GetDirectoryName( shaders ) );
		Watch( shaders );
		return compiler = new SlangCompiler( Path.Combine( game, "bin", "win64" ), [shaders] )
		{
			CacheFolder = CacheFolder ?? Path.Combine( game, ".source2", "borealissurfaces" ),
		};
	}

	/// <summary>The engine's core shaders: in the content root with the surface library (a map's own package comes first in its content).</summary>
	string CoreShaders()
	{
		var core = device.Content.Roots.FirstOrDefault( r => File.Exists( Path.Combine( r, "shaders", "sbox", "surface.slang" ) ) ) ?? device.Content.Roots.FirstOrDefault()
			?? throw new InvalidOperationException( "No content folder to find the engine's shaders in" );
		return Path.Combine( core, "shaders" );
	}

	/// <summary>
	/// Where compiled surfaces are kept between runs; <c>game/.source2/borealissurfaces</c> beside the engine's own caches
	/// unless set before the first compile.
	/// </summary>
	public string CacheFolder { get; set; }

	readonly List<FileSystemWatcher> watchers = new();
	readonly List<SurfaceProgram> retired = new();

	/// <summary>
	/// Hotload: a change to any file a program was compiled from (its module, a module it imports, a core include) drops
	/// the program, so the next draw of its materials compiles it again (from the cache when nothing it read changed).
	/// The dropped program stays alive until the device is disposed: frames in flight may still draw with it.
	/// </summary>
	void Watch( string folder )
	{
		if ( !Directory.Exists( folder ) ) return;
		lock ( watchers )
		{
			if ( watchers.Exists( w => string.Equals( w.Path, folder, StringComparison.OrdinalIgnoreCase ) ) ) return;
			var watcher = new FileSystemWatcher( folder ) { IncludeSubdirectories = true, NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size };
			watcher.Changed += ( _, e ) => Changed( e.FullPath );
			watcher.Created += ( _, e ) => Changed( e.FullPath );
			watcher.Renamed += ( _, e ) => Changed( e.FullPath );
			watcher.EnableRaisingEvents = true;
			watchers.Add( watcher );
		}
	}

	/// <summary>Programs compiled again after a source change, for reports and tests.</summary>
	public int Hotloads => hotloads;

	int hotloads;

	void Changed( string path )
	{
		path = Path.GetFullPath( path );
		lock ( programs )
		{
			foreach ( var (file, program) in programs.ToArray() )
			{
				if ( program is null )
				{
					// One that failed: try again after any change
					programs.Remove( file );
					continue;
				}

				if ( !program.Compiled.Dependencies.Contains( path, StringComparer.OrdinalIgnoreCase ) ) continue;
				programs.Remove( file );
				program.Stale = true;
				retired.Add( program );
				hotloads++;
			}
		}
	}

	/// <summary>
	/// A package's shader folder: searched like <see cref="AddFolder"/>, watched for changes, and every surface and raw
	/// module in it compiled now on a worker, so its materials' first draws find them ready. Errors go to <see cref="Error"/>.
	/// </summary>
	public System.Threading.Tasks.Task Mount( string folder )
	{
		folder = Path.GetFullPath( folder );
		AddFolder( folder );
		Watch( folder );
		var files = Directory.Exists( folder ) ? Directory.GetFiles( folder, "*.slang" ) : [];
		return System.Threading.Tasks.Task.Run( () =>
		{
			foreach ( var file in files ) Program( Path.GetFileName( file ) );
		} );
	}

	/// <summary>Compile a shader by file name (or path) and keep it; null, after reporting, when it doesn't compile.</summary>
	public CompiledShader Compile( string file ) => Program( file )?.Compiled;

	/// <summary>A material's program, compiled on first use; null when it can't draw.</summary>
	internal SurfaceProgram For( SurfaceMaterial material )
	{
		// Kept on the material once found: draws ask for it every frame
		if ( material.Program is { Stale: false } known && ReferenceEquals( known.Device, device ) ) return known;

		var program = Program( material.Shader );
		if ( program is null ) return null;
		if ( program.LayoutHash != material.LayoutHash || program.Compiled.Material.Size != material.RecordSize )
		{
			if ( program.Mismatched.Add( material.GetType() ) ) Error?.Invoke( $"{material.Shader}: {material.GetType().Name} was generated for another layout of {program.Compiled.Material.Type}; regenerate it" );
			return null;
		}

		material.Program = program;
		return program;
	}

	SurfaceProgram Program( string file )
	{
		lock ( programs )
		{
			if ( programs.TryGetValue( file, out var known ) ) return known;

			var path = File.Exists( file ) ? file : folders.Select( f => Path.Combine( f, file ) ).FirstOrDefault( File.Exists );
			SurfaceProgram program = null;
			if ( path is null )
			{
				Error?.Invoke( $"{file}: no such shader in {string.Join( ", ", folders )}" );
			}
			else
			{
				try
				{
					var raw = System.Text.RegularExpressions.Regex.IsMatch( File.ReadAllText( path ), @"\[\s*shader\s*\(" );
					var compiled = raw ? Compiler().CompileRaw( path ) : Compiler().CompileSurface( path );
					program = new SurfaceProgram( device, compiled );
					if ( compiled.GpuDrivenError is { } gpuDrivenError ) Error?.Invoke( $"{file}: no GPU-driven passes, its draws go the classic way: {gpuDrivenError}" );
				}
				catch ( ShaderCompileException e )
				{
					Error?.Invoke( $"{file}: {e.Errors}" );
				}
			}

			programs[file] = program;
			return program;
		}
	}

	/// <summary>Destroy what was made.</summary>
	public void Dispose()
	{
		lock ( watchers )
		{
			foreach ( var watcher in watchers ) watcher.Dispose();
			watchers.Clear();
		}

		lock ( programs )
		{
			foreach ( var program in programs.Values ) program?.Dispose();
			foreach ( var program in retired ) program.Dispose();
			programs.Clear();
			retired.Clear();
		}

		Records.Dispose();
	}
}

/// <summary>A compiled new-tier shader on a device: per pass, its modules and binding layouts, and pipelines made once.</summary>
internal sealed class SurfaceProgram : IDisposable
{
	readonly BackendDevice device;
	readonly Dictionary<string, PassObjects> passes = new();
	readonly Dictionary<(PassObjects Pass, VertexLayout Layout, Format Color, Format Depth, int Samples, ulong Constants, bool Ids, bool OpaqueFade, SurfaceRenderState State), PipelineHandle> pipelines = new();

	/// <summary>Material classes already reported as generated for another layout.</summary>
	public readonly HashSet<Type> Mismatched = new();

	public CompiledShader Compiled { get; }
	public uint LayoutHash { get; }

	public sealed class PassObjects
	{
		public CompiledPass Pass;
		public ShaderHandle Vertex, Pixel;
		public BindingLayoutHandle[] Sets;
	}

	/// <summary>The device it's made on.</summary>
	public BackendDevice Device => device;

	/// <summary>Dropped by a hotload: its materials look their program up again.</summary>
	public bool Stale
	{
		get => System.Threading.Volatile.Read( ref stale );
		set => System.Threading.Volatile.Write( ref stale, value );
	}

	bool stale;

	public SurfaceProgram( BackendDevice device, CompiledShader compiled )
	{
		this.device = device;
		Compiled = compiled;
		LayoutHash = MaterialClassWriter.Hash( compiled.Material );

		var modules = new Dictionary<CompiledStage, ShaderHandle>();
		ShaderHandle Module( CompiledStage stage )
		{
			if ( stage is null ) return default;
			if ( !modules.TryGetValue( stage, out var module ) ) modules[stage] = module = device.Gpu.CreateShader( stage.Spirv, $"{compiled.Name} {stage.Entry}" );
			return module;
		}

		foreach ( var (name, pass) in compiled.Passes )
		{
			var sets = new BindingLayoutHandle[ShaderAbi.BindlessSet];
			foreach ( var (stage, flags) in new[] { (pass.Vertex, ShaderStages.Vertex), (pass.Pixel, ShaderStages.Fragment) } )
			{
				if ( stage is null ) continue;
				var bySet = stage.Bindings.Where( b => b.Set < ShaderAbi.BindlessSet ).GroupBy( b => b.Set );
				foreach ( var group in bySet )
				{
					var slots = group.Select( b => new BindingSlot( b.Binding, Type( b ), flags, Math.Max( b.Count, 1 ) ) ).ToArray();
					sets[group.Key] = device.Gpu.CreateBindingLayout( slots );
				}
			}

			passes[name] = new PassObjects { Pass = pass, Vertex = Module( pass.Vertex ), Pixel = Module( pass.Pixel ), Sets = sets };
		}
	}

	static BindingType Type( ShaderBinding binding ) => binding.Kind switch
	{
		BindingKind.ConstantBuffer or BindingKind.Globals => BindingType.UniformBuffer,
		BindingKind.Buffer or BindingKind.MaterialRecords => BindingType.StorageBuffer,
		BindingKind.Sampler => BindingType.Sampler,
		BindingKind.StorageTexture => BindingType.StorageTexture,
		_ => BindingType.SampledTexture,
	};

	/// <summary>A pass by name, or null.</summary>
	public PassObjects Pass( string name ) => passes.TryGetValue( name, out var pass ) ? pass : null;

	readonly PassObjects[] gpuDrivenPasses = new PassObjects[16];
	readonly bool[] gpuDrivenLooked = new bool[16];

	/// <summary>
	/// A GPU-driven pass (<c>[Depth|Forward][AlphaTest|Translucent]GpuDriven[Lightmapped]</c>), found once; null when
	/// there's none. <paramref name="translucent"/> is the colour pass of blended materials (S_TRANSLUCENT).
	/// </summary>
	public PassObjects GpuDrivenPass( bool noColor, bool alphaTest, bool lightmapped, bool translucent = false )
	{
		translucent &= !noColor;
		var index = (translucent ? 8 : 0) | (noColor ? 4 : 0) | (alphaTest && !translucent ? 2 : 0) | (lightmapped ? 1 : 0);
		if ( System.Threading.Volatile.Read( ref gpuDrivenLooked[index] ) ) return gpuDrivenPasses[index];

		var name = (noColor ? (alphaTest ? ShaderPasses.DepthAlphaTest : ShaderPasses.Depth) : translucent ? ShaderPasses.Forward + "Translucent" : (alphaTest ? ShaderPasses.Forward + "AlphaTest" : ShaderPasses.Forward)) + ShaderPasses.GpuDriven;
		var found = (lightmapped ? Pass( name + ShaderPasses.Lightmapped ) : null) ?? Pass( name );
		gpuDrivenPasses[index] = found;
		System.Threading.Volatile.Write( ref gpuDrivenLooked[index], true );
		return found;
	}

	/// <summary>
	/// Whether its vertex stage moves vertices (<c>IVertexSurface</c>, <c>IInterpolatedVertexSurface</c>): when not, its
	/// positions alone place them.
	/// </summary>
	public bool Displaces => Compiled.Generated is { } generated && (generated.Contains( "SurfaceVertexDisplaced", StringComparison.Ordinal ) || generated.Contains( "SurfaceOutputDisplacedWith", StringComparison.Ordinal ));

	/// <summary>
	/// A pipeline for a pass drawing vertices laid out as <paramref name="layout"/> into these formats, with a material's
	/// resolved render state <paramref name="state"/> in colour passes (its blending, depth test and write, culling);
	/// depth is tested greater-or-equal (reverse Z) and alpha-tested passes use alpha to coverage with multisampling, as
	/// today's shaders do. Depth-only passes cull by the state's two-sidedness and test and write depth.
	/// Mesh vertices are stream 0, instance ids stream 1, the default stream 2, as for classic draws. Specialization
	/// constants (<c>SpecializedConstant()</c> globals) are per pipeline, <paramref name="constantsKey"/> their values.
	/// With <paramref name="ids"/>, the visibility-driven forward pass's id prepass: target 0 an R8 scratch (its alpha drives
	/// alpha to coverage), target 1 the ids, as BackendDevice.Pipeline makes them.
	/// </summary>
	public PipelineHandle Pipeline( PassObjects pass, VertexLayout layout, Format color, Format depth, int samples, SpecializationConstant[] vertexConstants, SpecializationConstant[] pixelConstants, ulong constantsKey, SurfaceRenderState state, bool ids = false, bool opaqueFade = false )
	{
		lock ( pipelines )
		{
			var key = (pass, layout, color, depth, samples, constantsKey, ids, opaqueFade, state);
			if ( pipelines.TryGetValue( key, out var pipeline ) ) return pipeline;

			// A vertex-cache draw reads its vertex ids from streams 2 and 3, as floats, and the default stream from 4, as
			// BackendDevice.Pipeline maps classic variants
			var vertexCache = layout.VertexCache;
			var inputs = pass.Pass.Vertex.Inputs.Select( i => new VertexInput( i.Location, Semantic( i.Semantic ), i.SemanticIndex, i.Kind == ScalarKind.Float ? SpirvScalar.Float : i.Kind == ScalarKind.Int ? SpirvScalar.Int : SpirvScalar.UInt, i.Components ) ).ToList();
			var attributes = ShaderDefaults.MapVertices( inputs, input =>
			{
				if ( input.Semantic == VertexSemantic.TexCoord && input.SemanticIndex == 13 ) return new VertexAttribute( 0, 1, VertexFormat.UInt, 0 );
				if ( vertexCache && input.Semantic == VertexSemantic.TexCoord && input.SemanticIndex is 14 or 15 ) return new VertexAttribute( 0, input.SemanticIndex == 14 ? 2 : 3, VertexFormat.Float, 0 );
				if ( layout.Find( input.Semantic, input.SemanticIndex ) is not { } field ) return null;
				var integer = field.Format is VertexFormat.UByte4 or VertexFormat.UInt or VertexFormat.UInt2 or VertexFormat.UInt4;
				if ( integer != (input.Kind != SpirvScalar.Float) ) return null;
				return new VertexAttribute( 0, 0, field.Format, field.Offset );
			}, defaultBuffer: vertexCache ? 4 : 2 );

			var depthOnly = color == Format.Unknown && !ids;
			var pixel = pass.Pixel.IsValid;
			var info = new GraphicsPipelineInfo
			{
				VertexShader = pass.Vertex,
				VertexEntry = pass.Pass.Vertex.Entry,
				VertexConstants = vertexConstants,
				FragmentConstants = pixel ? pixelConstants : null,
				FragmentShader = pixel ? pass.Pixel : default,
				FragmentEntry = pixel ? pass.Pass.Pixel.Entry : null,
				Sets = pass.Sets,
				VertexBuffers = vertexCache
					? [new( layout.Stride ), new( 4, VertexStepRate.Instance ), new( 4 ), new( 4 ), new( 0 )]
					: [new( layout.Stride ), new( 4, VertexStepRate.Instance ), new( 0 )],
				Attributes = attributes,
				Topology = PrimitiveTopology.TriangleList,
				CullMode = state.TwoSided ? CullMode.None : CullMode.Back,
				FrontFace = FrontFace.CounterClockwise,
				DepthBias = depthOnly || ids,
				DepthTest = depth != Format.Unknown,
				DepthWrite = depth != Format.Unknown,
				DepthCompare = CompareFunction.GreaterEqual,
				Blend = ids ? [BlendState.Opaque, BlendState.Opaque] : [BlendState.Opaque],

				// Alpha-tested passes write today's alpha-to-coverage alpha (S_ALPHA_TEST), with multisampling only, as the
				// .shaders' AlphaToCoverageEnable render state does; so does a fading object's (D_OPAQUE_FADE), unless it blends
				AlphaToCoverage = samples > 1 && !state.DrawsTranslucent && (opaqueFade || pass.Pass.Name.Contains( "AlphaTest", StringComparison.Ordinal )),
				ColorFormats = ids ? [Format.R8Unorm, Format.R32Uint] : depthOnly ? [] : [color],
				DepthFormat = depth,
				Samples = samples,
				Name = $"{Compiled.Name} {pass.Pass.Name}",
			};

			if ( !depthOnly && !ids )
			{
				info.Blend = [Blend( state.Blend )];
				info.DepthTest = depth != Format.Unknown && state.DepthTest;
				info.DepthWrite = depth != Format.Unknown && state.DepthWrite;
			}

			pipeline = device.Gpu.CreateGraphicsPipeline( info );
			pipelines[key] = pipeline;
			return pipeline;
		}
	}

	/// <summary>
	/// A blend mode's colour blending; alpha is blended as today's shaders leave it (source alpha, one), premultiplied
	/// alpha as glass sets it (one, one minus source alpha).
	/// </summary>
	internal static BlendState Blend( SurfaceBlend blend )
	{
		var (source, destination) = blend switch
		{
			SurfaceBlend.Alpha => (BlendFactor.SourceAlpha, BlendFactor.OneMinusSourceAlpha),
			SurfaceBlend.Premultiplied => (BlendFactor.One, BlendFactor.OneMinusSourceAlpha),
			SurfaceBlend.Additive => (BlendFactor.SourceAlpha, BlendFactor.One),
			SurfaceBlend.Multiply => (BlendFactor.Zero, BlendFactor.SourceColor),
			SurfaceBlend.Mod2x => (BlendFactor.DestinationColor, BlendFactor.SourceColor),
			SurfaceBlend.ModulateAdd => (BlendFactor.DestinationColor, BlendFactor.One),
			_ => (BlendFactor.One, BlendFactor.Zero),
		};

		if ( blend == SurfaceBlend.Opaque ) return BlendState.Opaque;
		var premultiplied = blend == SurfaceBlend.Premultiplied;
		return new BlendState
		{
			Enable = true,
			SourceColor = source,
			DestinationColor = destination,
			SourceAlpha = premultiplied ? BlendFactor.One : BlendFactor.SourceAlpha,
			DestinationAlpha = premultiplied ? BlendFactor.OneMinusSourceAlpha : BlendFactor.One,
		};
	}

	internal static VertexSemantic Semantic( string name ) => name switch
	{
		"POSITION" => VertexSemantic.Position,
		"BLENDWEIGHT" => VertexSemantic.BlendWeight,
		"BLENDINDICES" => VertexSemantic.BlendIndices,
		"NORMAL" => VertexSemantic.Normal,
		"TANGENT" => VertexSemantic.Tangent,
		"BINORMAL" => VertexSemantic.Binormal,
		"COLOR" => VertexSemantic.Color,
		_ => VertexSemantic.TexCoord,
	};

	public void Dispose()
	{
		lock ( pipelines )
		{
			foreach ( var pipeline in pipelines.Values ) device.Gpu.Destroy( pipeline );
			pipelines.Clear();
		}
	}
}

/// <summary>
/// Every surface material's record, in one storage buffer the shaders read at a per-draw offset
/// (<c>g_SurfaceMaterials</c>, <c>sbox/material.slang</c>). A record is written when its material is first drawn and again,
/// at a new offset, when it changes; the buffer only grows, so frames in flight keep reading what they bound. This is
/// the one place that knows where records live: gpudriven's material table (binding 63) replaces it when it lands.
/// </summary>
internal sealed class SurfaceRecords( Device gpu ) : IDisposable
{
	readonly Dictionary<SurfaceMaterial, (int Version, uint Offset)> written = new( ReferenceEqualityComparer.Instance );
	readonly List<BufferHandle> retired = new();
	BufferHandle buffer;
	long size, used;

	/// <summary>The buffer, valid once a record is written.</summary>
	public BufferHandle Buffer
	{
		get
		{
			lock ( written ) return buffer;
		}
	}

	/// <summary>Where <paramref name="material"/>'s current record is, written now if it changed.</summary>
	public uint Offset( SurfaceMaterial material )
	{
		lock ( written )
		{
			var version = material.Version;
			if ( written.TryGetValue( material, out var known ) && known.Version == version ) return known.Offset;

			var bytes = (Math.Max( material.RecordSize, 4 ) + 15) & ~15;
			if ( !buffer.IsValid || used + bytes > size ) Grow( used + bytes );

			var offset = (uint)used;
			Span<byte> record = stackalloc byte[bytes];
			record.Clear();
			material.WriteRecord( record, static ( texture, srgb ) => texture.ViewIndex( srgb, TextureViewDimension.Default ) );
			record.CopyTo( gpu.Map( buffer )[(int)offset..] );
			used += bytes;
			written[material] = (version, offset);
			return offset;
		}
	}

	void Grow( long needed )
	{
		var next = Math.Max( 64 * 1024, size * 2 );
		while ( next < needed ) next *= 2;
		var grown = gpu.CreateBuffer( new BufferInfo { Size = next, Usage = BufferUsage.Storage, Memory = MemoryLocation.Upload, Name = "Borealis surface material records" } );
		if ( buffer.IsValid )
		{
			// The old buffer is kept until the records are disposed: draws recorded earlier this frame bound it, and a
			// destroy is only deferred past what's been submitted. It only doubles a few times, so this is at most its size.
			gpu.Map( buffer )[..(int)used].CopyTo( gpu.Map( grown ) );
			retired.Add( buffer );
		}

		buffer = grown;
		size = next;
	}

	public void Dispose()
	{
		lock ( written )
		{
			if ( buffer.IsValid ) gpu.Destroy( buffer );
			foreach ( var old in retired ) gpu.Destroy( old );
			retired.Clear();
			buffer = default;
		}
	}
}
