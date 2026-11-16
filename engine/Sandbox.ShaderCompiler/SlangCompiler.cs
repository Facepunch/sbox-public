using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Sandbox.ShaderCompiler.Slang;

namespace Sandbox.ShaderCompiler;

/// <summary>
/// Compiles new-tier shaders with the engine's Slang. A surface (<c>[Material]</c> struct implementing <c>ISurface</c>)
/// gets the engine's passes, instantiated for it in a generated instance file; a raw module is compiled as written, its
/// <c>[shader]</c> entry points mapped to passes by name. Both come out the same: SPIR-V per pass, bindings, and the
/// material record's layout. See docs/managed/shader-format-replacement.md.
/// </summary>
public sealed partial class SlangCompiler
{
	static readonly object compileLock = new();

	readonly string slangDirectory;
	readonly List<string> searchPaths;

	/// <summary>
	/// A compiler using <c>slang.dll</c> from <paramref name="slangDirectory"/> (the engine's <c>game/bin/win64</c>),
	/// finding includes and modules in <paramref name="searchPaths"/> (the engine's <c>game/core/shaders</c> first).
	/// </summary>
	public SlangCompiler( string slangDirectory, IEnumerable<string> searchPaths )
	{
		this.slangDirectory = slangDirectory;
		this.searchPaths = searchPaths.Select( p => Path.GetFullPath( p ) ).ToList();
	}

	/// <summary>
	/// Where compiled shaders are kept between runs (null: nowhere). An entry is used only while the compiler (this
	/// assembly's build and its Slang), the search paths, the source and every file the compile read are as they were.
	/// </summary>
	public string CacheFolder { get; init; }

	/// <summary>Every file the current compile has read, for its <see cref="CompiledShader.Dependencies"/>.</summary>
	HashSet<string> readFiles;

	CompiledShader Cached( string path, ShaderTier tier, Func<string, CompiledShader> compile )
	{
		path = Path.GetFullPath( path );
		if ( CacheFolder is null ) return compile( path );

		var text = File.ReadAllText( path );
		var context = $"{slangDirectory}|{string.Join( ';', searchPaths )}";
		if ( CompileCache.Load( CacheFolder, context, slangDirectory, tier, path, text ) is { } cached ) return cached;

		var compiled = compile( path );
		CompileCache.Save( CacheFolder, context, slangDirectory, path, text, compiled );
		return compiled;
	}

	/// <summary>Defines vfx gives every program, which the core includes test.</summary>
	static Dictionary<string, string> Defines( StageKind stage ) => new()
	{
		["SHADER_MODEL"] = "60",
		["PROGRAM"] = stage switch { StageKind.Vertex => "1", StageKind.Compute => "6", _ => "2" },
		["D3D"] = "1",
		["VULKAN"] = "1",
		["DXC"] = "1",
		["SLANG"] = "1",
	};

	/// <summary>
	/// What Slang compiles for each file: vfx's include rewrite for HLSL, the material rewrite for modules, whose globals'
	/// annotations (a sampler's <c>Filter</c>) are recorded as the includes' are.
	/// </summary>
	static string Rewrite( string path, string text, Dictionary<string, Annotations> annotations )
	{
		if ( path.EndsWith( ".slang", StringComparison.OrdinalIgnoreCase ) ) return VfxIncludes.Rewrite( MaterialSource.Rewrite( text, out _ ), annotations );
		if ( path.EndsWith( ".slangh", StringComparison.OrdinalIgnoreCase ) ) return text;
		return VfxIncludes.Rewrite( text, annotations );
	}

	SlangRequest Request( StageKind stage, IReadOnlyList<string> extraSearchPaths, IReadOnlyDictionary<string, string> extraDefines = null )
	{
		var session = SlangApi.Session( slangDirectory );
		Dictionary<string, Annotations> annotations = null;
		var defines = Defines( stage );
		foreach ( var (name, value) in extraDefines ?? new Dictionary<string, string>() ) defines[name] = value;
		var request = new SlangRequest( session, [.. extraSearchPaths, .. searchPaths], defines, stage == StageKind.Vertex, SpirvOptimizer.Available( slangDirectory ), ( path, text ) =>
		{
			readFiles?.Add( Path.GetFullPath( path ) );
			return Rewrite( path, text, annotations );
		} );
		annotations = request.Annotations;
		return request;
	}

	/// <summary>
	/// Compile a surface: the module at <paramref name="path"/> with one <c>[Material]</c> struct implementing
	/// <c>ISurface</c>. Throws <see cref="ShaderCompileException"/> with Slang's messages on errors.
	/// </summary>
	public CompiledShader CompileSurface( string path ) => Cached( path, ShaderTier.Surface, CompileSurfaceNow );

	CompiledShader CompileSurfaceNow( string path )
	{
		var clock = Stopwatch.StartNew();
		var source = MaterialSource.Rewrite( File.ReadAllText( path ), out var declared );
		if ( declared.Count != 1 ) throw new ShaderCompileException( path, $"A surface module declares one [Material] struct; this one declares {declared.Count}" );

		var material = declared[0];
		var directory = Path.GetDirectoryName( path );
		var module = Path.GetFileNameWithoutExtension( path );
		if ( !ModuleName().IsMatch( module ) ) throw new ShaderCompileException( path, $"{module} isn't a valid module name: name surface files like identifiers" );

		lock ( compileLock )
		{
			readFiles = new( StringComparer.OrdinalIgnoreCase ) { path };
			var layout = ReflectMaterial( path, source, material.Name, directory );
			var interpolatedDisplaces = material.Interfaces.Contains( "IInterpolatedVertexSurface" );
			var displaces = interpolatedDisplaces || material.Interfaces.Contains( "IVertexSurface" );
			var lit = material.Interfaces.Contains( "ILitSurface" );
			var interpolatedCombiner = material.Interfaces.Contains( "IInterpolatedCombinerSurface" );
			var interpolated = interpolatedCombiner || interpolatedDisplaces || material.Interfaces.Contains( "IInterpolatedSurface" );
			var sunLit = material.Interfaces.Contains( "ISunLitSurface" );
			var composite = material.Interfaces.Contains( "ICompositeSurface" );
			var layered = material.Interfaces.Contains( "ILayeredSurface" );
			var combiner = layered || interpolatedCombiner || material.Interfaces.Contains( "ICombinerSurface" );
			if ( !displaces && !lit && !interpolated && !sunLit && !composite && !combiner && !material.Interfaces.Contains( "ISurface" ) )
				throw new ShaderCompileException( path, $"{material.Name} is a [Material] struct but implements none of ISurface, IInterpolatedSurface, IVertexSurface, ILitSurface, ISunLitSurface, ICompositeSurface, ICombinerSurface, IInterpolatedCombinerSurface or ILayeredSurface" );
			if ( layered && interpolated )
				throw new ShaderCompileException( path, $"{material.Name}: an ILayeredSurface can't also be an IInterpolatedSurface yet" );
			if ( combiner && (lit || sunLit || composite || (interpolated && !interpolatedCombiner)) )
				throw new ShaderCompileException( path, $"{material.Name}: an ICombinerSurface can't also be an ILitSurface, ISunLitSurface, ICompositeSurface or IInterpolatedSurface yet (an IInterpolatedCombinerSurface is both)" );
			if ( interpolated && ((displaces && !interpolatedDisplaces) || lit || sunLit || composite) )
				throw new ShaderCompileException( path, $"{material.Name}: an IInterpolatedSurface can't also be an IVertexSurface (it displaces as an IInterpolatedVertexSurface), ILitSurface, ISunLitSurface or ICompositeSurface yet" );
			if ( lit && sunLit )
				throw new ShaderCompileException( path, $"{material.Name}: a surface is an ILitSurface (every light) or an ISunLitSurface (the sun alone), not both" );
			if ( composite && (lit || sunLit) )
				throw new ShaderCompileException( path, $"{material.Name}: an ICompositeSurface can't also be an ILitSurface or ISunLitSurface yet" );

			var defines = CombinerDefines( source );
			if ( defines.Count > 0 && !combiner ) throw new ShaderCompileException( path, $"{material.Name}: [Combiner( ... )] is for an ICombinerSurface or IInterpolatedCombinerSurface" );
			var instance = InstanceFile( module, material.Name, displaces, lit, layout.Fields.FirstOrDefault( f => f.Opacity )?.Name, interpolated, sunLit, composite, combiner, defines, layout.Features, layered );
			var instancePath = Path.Combine( directory, $"{module}.instance.slang" );

			var vertex = CompileEntry( instancePath, instance, "MainVs", StageKind.Vertex, directory );
			var vertexLightmapped = CompileEntry( instancePath, instance, "MainVs", StageKind.Vertex, directory, Lightmapped );
			var forward = CompileEntry( instancePath, instance, "ForwardPs", StageKind.Pixel, directory );
			var forwardAlphaTest = CompileEntry( instancePath, instance, "ForwardAlphaTestPs", StageKind.Pixel, directory, AlphaTested );
			var depthAlphaTest = CompileEntry( instancePath, instance, "DepthAlphaTestPs", StageKind.Pixel, directory, AlphaTestedDepth );
			var forwardTranslucent = CompileEntry( instancePath, instance, "ForwardTranslucentPs", StageKind.Pixel, directory, Translucent );
			var depthVertex = CompileEntry( instancePath, instance, "DepthVs", StageKind.Vertex, directory );
			var vertexSkinned = CompileEntry( instancePath, instance, "MainVs", StageKind.Vertex, directory, VertexAnimation );
			var depthVertexSkinned = CompileEntry( instancePath, instance, "DepthVs", StageKind.Vertex, directory, VertexAnimation );

			// Each pass twice: as compiled, and with the vertex stage that passes on the mesh's lightmap uv
			var passes = new Dictionary<string, CompiledPass>();
			string gpuDrivenError = null;
			foreach ( var (v, suffix) in new[] { (vertex, ""), (vertexLightmapped, ShaderPasses.Lightmapped) } )
			{
				foreach ( var (name, pixel) in new[] { (ShaderPasses.Forward, forward), (ShaderPasses.Forward + "AlphaTest", forwardAlphaTest), (ShaderPasses.Forward + "Translucent", forwardTranslucent), (ShaderPasses.Depth, null), (ShaderPasses.DepthAlphaTest, depthAlphaTest) } )
					passes[name + suffix] = new() { Name = name + suffix, Vertex = name == ShaderPasses.Depth ? depthVertex : v, Pixel = pixel };
			}

			// And with the vertex stage of skinned draws from the vertex cache, which reads the skinned vertices by vertex id
			foreach ( var (name, pixel) in new[] { (ShaderPasses.Forward, forward), (ShaderPasses.Forward + "AlphaTest", forwardAlphaTest), (ShaderPasses.Forward + "Translucent", forwardTranslucent), (ShaderPasses.Depth, null), (ShaderPasses.DepthAlphaTest, depthAlphaTest) } )
				passes[name + ShaderPasses.Skinned] = new() { Name = name + ShaderPasses.Skinned, Vertex = name == ShaderPasses.Depth ? depthVertexSkinned : vertexSkinned, Pixel = pixel };

			// The same passes GPU-driven, each with a vertex entry generated from what the classic one reads; a surface
			// whose GPU-driven compile fails draws the classic way
			try
			{
				var gpuVertex = CompileEntry( instancePath, instance + GpuDrivenVertex( material.Name, displaces, interpolated, vertex.Inputs ), "MainVsGpuDriven", StageKind.Vertex, directory, GpuDriven );
				var gpuVertexLightmapped = CompileEntry( instancePath, instance + GpuDrivenVertex( material.Name, displaces, interpolated, vertexLightmapped.Inputs ), "MainVsGpuDriven", StageKind.Vertex, directory, With( GpuDriven, Lightmapped ) );
				var gpuForward = CompileEntry( instancePath, instance, "ForwardPsGpuDriven", StageKind.Pixel, directory, GpuDriven );
				var gpuForwardAlphaTest = CompileEntry( instancePath, instance, "ForwardAlphaTestPsGpuDriven", StageKind.Pixel, directory, With( GpuDriven, AlphaTested ) );
				var gpuDepthAlphaTest = CompileEntry( instancePath, instance, "DepthAlphaTestPsGpuDriven", StageKind.Pixel, directory, With( GpuDriven, AlphaTestedDepth ) );
				var gpuForwardTranslucent = CompileEntry( instancePath, instance, "ForwardTranslucentPsGpuDriven", StageKind.Pixel, directory, With( GpuDriven, Translucent ) );
				foreach ( var (v, suffix) in new[] { (gpuVertex, ShaderPasses.GpuDriven), (gpuVertexLightmapped, ShaderPasses.GpuDriven + ShaderPasses.Lightmapped) } )
				{
					foreach ( var (name, pixel) in new[] { (ShaderPasses.Forward, gpuForward), (ShaderPasses.Forward + "AlphaTest", gpuForwardAlphaTest), (ShaderPasses.Forward + "Translucent", gpuForwardTranslucent), (ShaderPasses.Depth, null), (ShaderPasses.DepthAlphaTest, gpuDepthAlphaTest) } )
						passes[name + suffix] = new() { Name = name + suffix, Vertex = v, Pixel = pixel };
				}
			}
			catch ( ShaderCompileException e )
			{
				gpuDrivenError = e.Errors;
			}

			return new CompiledShader
			{
				Name = material.Name,
				Tier = ShaderTier.Surface,
				Path = path,
				Material = layout,
				Passes = passes,
				GpuDrivenError = gpuDrivenError,
				Generated = instance,
				Milliseconds = clock.Elapsed.TotalMilliseconds,
				Dependencies = readFiles.Order( StringComparer.OrdinalIgnoreCase ).ToList(),
			};
		}
	}

	/// <summary>
	/// Compile a raw module: its author owns the entry points. <c>MainVs</c> and <c>MainPs</c> make the forward pass,
	/// <c>MainVs</c> alone the depth pass, and <c>DepthPs</c>, when there is one, the alpha-tested depth pass. Its
	/// parameters are a <c>[Material]</c> struct read with <c>LoadMaterial&lt;T&gt;()</c>, as surfaces' are.
	/// </summary>
	public CompiledShader CompileRaw( string path ) => Cached( path, ShaderTier.Raw, CompileRawNow );

	CompiledShader CompileRawNow( string path )
	{
		var clock = Stopwatch.StartNew();
		var original = File.ReadAllText( path );
		var source = MaterialSource.Rewrite( original, out var declared );
		if ( declared.Count > 1 ) throw new ShaderCompileException( path, $"A raw module declares at most one [Material] struct; this one declares {declared.Count}" );

		var entries = EntryPoints().Matches( original ).Select( m => (Stage: m.Groups["stage"].Value, Name: m.Groups["name"].Value) ).ToList();
		bool Has( string name, string stage ) => entries.Exists( e => e.Name == name && e.Stage == stage );
		if ( !Has( "MainVs", "vertex" ) || !Has( "MainPs", "pixel" ) ) throw new ShaderCompileException( path, "A raw module has [shader(\"vertex\")] MainVs and [shader(\"pixel\")] MainPs" );

		var directory = Path.GetDirectoryName( path );
		lock ( compileLock )
		{
			readFiles = new( StringComparer.OrdinalIgnoreCase ) { path };
			var layout = declared.Count == 1 ? ReflectMaterial( path, source, declared[0].Name, directory ) : MaterialLayout.Create( "", [] );
			var vertex = CompileEntry( path, source, "MainVs", StageKind.Vertex, directory );
			var forward = CompileEntry( path, source, "MainPs", StageKind.Pixel, directory );
			var passes = new Dictionary<string, CompiledPass>
			{
				[ShaderPasses.Forward] = new() { Name = ShaderPasses.Forward, Vertex = vertex, Pixel = forward },
				[ShaderPasses.Depth] = new() { Name = ShaderPasses.Depth, Vertex = vertex },
			};

			if ( Has( "DepthPs", "pixel" ) )
				passes[ShaderPasses.DepthAlphaTest] = new() { Name = ShaderPasses.DepthAlphaTest, Vertex = vertex, Pixel = CompileEntry( path, source, "DepthPs", StageKind.Pixel, directory ) };

			return new CompiledShader
			{
				Name = declared.Count == 1 ? declared[0].Name : Path.GetFileNameWithoutExtension( path ),
				Tier = ShaderTier.Raw,
				Path = path,
				Material = layout,
				Passes = passes,
				Milliseconds = clock.Elapsed.TotalMilliseconds,
				Dependencies = readFiles.Order( StringComparer.OrdinalIgnoreCase ).ToList(),
			};
		}
	}

	/// <summary>
	/// A combiner surface's <c>[Combiner( "A, B = C" )]</c>: the final combiner's compile-time features it lights with, each
	/// a define before the engine's includes (a name alone is 1).
	/// </summary>
	internal static List<(string Name, string Value)> CombinerDefines( string source )
	{
		var defines = new List<(string, string)>();
		foreach ( Match m in CombinerAttribute().Matches( source ) )
		{
			foreach ( var entry in m.Groups["defines"].Value.Split( ',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries ) )
			{
				var parts = entry.Split( '=', 2, StringSplitOptions.TrimEntries );
				defines.Add( (parts[0], parts.Length > 1 ? parts[1] : "1") );
			}
		}

		return defines;
	}

	[GeneratedRegex( @"\bCombiner\s*\(\s*""(?<defines>[^""]*)""\s*\)" )]
	private static partial Regex CombinerAttribute();

	[GeneratedRegex( @"^[A-Za-z_]\w*$" )]
	private static partial Regex ModuleName();

	[GeneratedRegex( @"\[\s*shader\s*\(\s*""(?<stage>\w+)""\s*\)\s*\][^(]*?\b(?<name>\w+)\s*\(" )]
	private static partial Regex EntryPoints();

	/// <summary>Phase 1: the module alone, to reflect its <c>[Material]</c> struct's fields.</summary>
	MaterialLayout ReflectMaterial( string path, string source, string type, string directory )
	{
		using var request = Request( StageKind.Pixel, [directory] );
		request.AddSource( path, source );
		request.SkipCodegen();
		request.Compile( path );
		return MaterialReflection.Read( request.Reflection, type, path );
	}

	/// <summary>Alpha-tested passes compile with today's alpha test (S_ALPHA_TEST): Shade runs alpha to coverage.</summary>
	static readonly Dictionary<string, string> AlphaTested = new() { ["S_ALPHA_TEST"] = "1" };

	/// <summary>The translucent forward pass compiles with today's S_TRANSLUCENT: opacity kept in alpha, no screen-space occlusion.</summary>
	static readonly Dictionary<string, string> Translucent = new() { ["S_TRANSLUCENT"] = "1" };

	/// <summary>
	/// The vertex stage of lightmapped draws: today's dynamic combo that reads the mesh's lightmap uv (TEXCOORD3) and passes
	/// it on. The pixel stage is the same; it samples the lightmap when the object has one.
	/// </summary>
	static readonly Dictionary<string, string> Lightmapped = new() { ["D_BAKED_LIGHTING_FROM_LIGHTMAP"] = "1" };

	/// <summary>
	/// The vertex stage of skinned draws from the vertex cache: today's dynamic combo that reads the skinned position and
	/// tangent frame from the cache by the draw's vertex ids (TEXCOORD14, TEXCOORD15).
	/// </summary>
	static readonly Dictionary<string, string> VertexAnimation = new() { ["D_CS_VERTEX_ANIMATION"] = "1" };

	/// <summary>GPU-driven passes (<c>sbox/surface_passes.slangh</c>, docs/managed/borealis-gpu-driven.md).</summary>
	static readonly Dictionary<string, string> GpuDriven = new() { ["SURFACE_GPU_DRIVEN"] = "1" };

	static Dictionary<string, string> With( Dictionary<string, string> a, Dictionary<string, string> b )
	{
		var both = new Dictionary<string, string>( a );
		foreach ( var (name, value) in b ) both[name] = value;
		return both;
	}

	/// <summary>The alpha-tested depth pass also in Depth mode: Shade returns the coverage before lighting.</summary>
	static readonly Dictionary<string, string> AlphaTestedDepth = new() { ["S_ALPHA_TEST"] = "1", ["S_MODE_DEPTH"] = "1" };

	CompiledStage CompileEntry( string path, string source, string entry, StageKind stage, string directory, IReadOnlyDictionary<string, string> defines = null )
	{
		using var request = Request( stage, [directory], defines );
		request.AddSource( path, source );
		request.AddEntryPoint( entry, stage );
		request.Compile( path );

		var spirv = request.Code() ?? throw new ShaderCompileException( path, $"{entry}: Slang returned no code. {request.Diagnostics}" );
		spirv = SpirvOptimizer.Optimize( slangDirectory, spirv, out _ );

		// The pixel stage's own resources go in set 1, as vfx puts them, so the two stages' bindings never collide
		if ( stage == StageKind.Pixel ) spirv = Spirv.MoveSet( spirv, 0, 1 );

		return StageReflection.Describe( stage, entry, spirv, request.ReflectionJson(), request.Annotations );
	}

	/// <summary>
	/// The instance file for a <c>[Material]</c> struct: the engine's passes, named as entry points. Each pixel entry loads
	/// the record at the offset the vertex stage passed on (or the draw's, per pipeline) and hands the passes the slot of
	/// the material's <c>[Opacity]</c> texture, when it has one. A lit surface's (<c>ILitSurface</c>) also turns on the
	/// engine light loop's hook (<c>SURFACE_LIGHT_HOOK</c>) and hands it the material, so <c>Light</c> runs per light. An
	/// interpolated surface's (<c>IInterpolatedSurface</c>) vertex stage also reads its <c>Varyings</c> and passes them on. A
	/// composite surface's (<c>ICompositeSurface</c>) forward passes call its <c>Composite</c> on the lit colour, and a
	/// combiner surface's (<c>ICombinerSurface</c>) shade through complex.shader's final combiner (<c>SURFACE_FINAL_COMBINER</c>),
	/// a layered one's (<c>ILayeredSurface</c>, <paramref name="layered"/>) through its own Shade over that lighting.
	/// Pixel entries read the material's <paramref name="features"/> as specialization constants instead of from the record
	/// when the pipeline is specialised for its material (<see cref="MaterialLayout.SpecializedId"/>), so its branches fold.
	/// </summary>
	internal static string InstanceFile( string module, string type, bool displaces, bool lit, string opacityField = null, bool interpolated = false, bool sunLit = false, bool composite = false, bool combiner = false, IReadOnlyList<(string Name, string Value)> combinerDefines = null, IReadOnlyList<MaterialField> features = null, bool layered = false )
	{
		var specialize = features is { Count: > 0 } ? " SurfaceSpecialize( m );" : "";
		var forward = sunLit ? "SurfaceForwardSunLit" : composite ? "SurfaceForwardComposite" : layered ? "SurfaceForwardLayered" : combiner ? "SurfaceForwardCombiner" : "SurfaceForward";
		var vertex = displaces ? $"SurfaceVertexDisplaced<{type}>( i )" : "SurfaceVertex( i )";
		var opacity = opacityField is null ? "0" : $"m.{opacityField}.Slot";
		var keep = lit ? " s_LitSurface = m;" : "";
		var output = interpolated ? $"SurfaceVertexOutputWith<{type}.Varyings>" : "SurfaceVertexOutput";
		var surface = interpolated ? $"{(combiner ? "SurfaceWithVaryingsCombiner" : "SurfaceWithVaryings")}<{type}>" : type;
		var wrap = interpolated ? $" {surface} s; s.Surface = m; s.Values = i.Varyings;" : "";
		var argument = interpolated ? "s" : "m";
		string Shade( bool alphaTest ) => $"{type} m = SurfaceRecord<{type}>( PixelMaterialOffset( i.MaterialOffset ) );{specialize}{keep}{wrap} return {forward}<{surface}, {(alphaTest ? "true" : "false")}>( i.Pixel, {argument}, {opacity} );";
		string Shaded( string entry, bool alphaTest ) => $"float4 Shaded{entry}( {output} i ) {{ {Shade( alphaTest )} }}\n";

		// The record offset (and on GPU-driven passes the draw) reaches the pixel stage flat, so per lane: shaded with it as
		// it is, every record field and texture slot would be read per lane, and each texture sample would loop over the
		// slots in the wave. The wave shades its distinct values one at a time instead, each wave-uniform. A quad's lanes
		// share a primitive, so a value; its helper lanes, which wave operations may leave out, take the turn from the
		// quad's other lanes, so derivatives stay whole. A wave holds at most 64 distinct values.
		static string ForEachInWave( string key, string body ) =>
			$"[loop] for ( uint n = 0; n < 64; n++ ) {{ uint first = WaveReadLaneFirst( {key} ); uint turn = first == {key} ? 1u : 0u; " +
			$"turn |= QuadReadAcrossX( turn ) | QuadReadAcrossY( turn ) | QuadReadAcrossDiagonal( turn ); if ( turn != 0 ) {{ {body} break; }} }}";

		// Every pixel entry takes the facing, so a two-sided material's back faces shade with the normal turned to the viewer
		string Pixel( string entry, bool alphaTest ) => Shaded( entry, alphaTest ) +
			$"[shader( \"pixel\" )] float4 {entry}( {output} i, bool front : SV_IsFrontFace ) : SV_Target0 {{ s_SurfaceFrontFace = front; float4 color = 0; {ForEachInWave( "i.MaterialOffset", $"{output} w = i; w.MaterialOffset = first; color = Shaded{entry}( w );" )} return color; }}\n";

		// GPU-driven: the draw's lighting record first; the alpha-tested depth pass also writes the visibility id (the vertex
		// bit + 1) at target 1, which the visibility pass's prepass reads and other passes have no target for. A draw has
		// one material record, so the draw is the value the wave goes through
		string GpuDrivenShade( string entry, string result ) =>
			ForEachInWave( "v.Draw", $"SurfaceFetchLighting( first ); {output} i = v.o; i.MaterialOffset = WaveReadLaneFirst( i.MaterialOffset ); {result} = Shaded{entry}( i );" );
		string GpuDrivenPixel( string entry, bool alphaTest, bool ids ) => Shaded( entry, alphaTest ) + (ids
			? $"[shader( \"pixel\" )] SurfaceIdsOutput {entry}( SurfaceGpuDrivenVertexOutput v, bool front : SV_IsFrontFace ) {{ s_SurfaceFrontFace = front; SurfaceIdsOutput o; o.Color = 0; {GpuDrivenShade( entry, "o.Color" )} o.Id = v.VertexBit + 1; return o; }}\n"
			: $"[shader( \"pixel\" )] float4 {entry}( SurfaceGpuDrivenVertexOutput v, bool front : SV_IsFrontFace ) : SV_Target0 {{ s_SurfaceFrontFace = front; float4 color = 0; {GpuDrivenShade( entry, "color" )} return color; }}\n");

		var text = new StringBuilder();
		text.Append( "// Generated by Sandbox.ShaderCompiler for the [Material] struct " ).Append( type ).Append( " in " ).Append( module ).Append( ".slang.\n" );
		if ( opacityField is not null ) text.Append( "#define SURFACE_OPACITY_TEXTURE 1\n" );
		if ( lit ) text.Append( "#define SURFACE_LIGHT_HOOK 1\n" );
		if ( combiner ) text.Append( "#define SURFACE_FINAL_COMBINER 1\n" );
		foreach ( var (name, value) in combinerDefines ?? [] ) text.Append( "#define " ).Append( name ).Append( ' ' ).Append( value ).Append( '\n' );
		text.Append( "#include \"sbox/surface_passes.slangh\"\n" );
		text.Append( "import " ).Append( module ).Append( ";\n\n" );
		text.Append( "#ifdef SURFACE_GPU_DRIVEN\n" );
		text.Append( "// What a GPU-driven vertex stage hands the pixel stage: the draw and the vertex's visibility bit, flat, first\n" );
		text.Append( "struct SurfaceGpuDrivenVertexOutput { nointerpolation uint Draw : BOREALIS_DRAW; nointerpolation uint VertexBit : BOREALIS_VERTEX; " ).Append( output ).Append( " o; };\n" );
		text.Append( "struct SurfaceIdsOutput { float4 Color : SV_Target0; uint Id : SV_Target1; };\n" );
		text.Append( "#endif\n\n" );
		text.Append( "#if ( PROGRAM == VFX_PROGRAM_VS )\n" );
		var varyingsInput = interpolated ? $", {type}.Varyings varyings" : "";
		text.Append( $"[shader( \"vertex\" )] {output} MainVs( VertexInput i{varyingsInput} ) {{ return {VertexStage( type, displaces, interpolated )}; }}\n" );
		text.Append( "// Opaque depth: the position alone, so its draws can read a positions-only vertex copy\n" );
		var depthInput = displaces && interpolated ? varyingsInput : "";
		var depthPosition = displaces && interpolated ? $"{VertexStage( type, displaces, interpolated )}.Pixel" : vertex;
		text.Append( $"[shader( \"vertex\" )] float4 DepthVs( VertexInput i{depthInput} ) : SV_Position {{ return {depthPosition}.vPositionPs; }}\n" );
		text.Append( "#else\n" );
		if ( lit )
		{
			text.Append( "// The engine's light loop calls SurfaceLight for every light it shades the pixel with\n" );
			text.Append( "static " ).Append( type ).Append( " s_LitSurface;\n" );
			text.Append( "float3 SurfaceLight( LightSample light ) { return s_LitSurface.Light( SurfaceLitMaterial(), SurfaceLitInput(), light ); }\n" );
		}

		if ( features is { Count: > 0 } ) text.Append( Specialization( type, features ) );

		text.Append( Pixel( "ForwardPs", false ) );
		text.Append( Pixel( "ForwardAlphaTestPs", true ) );
		text.Append( "// Compiled with S_TRANSLUCENT, for blended materials\n" );
		text.Append( Pixel( "ForwardTranslucentPs", false ) );
		text.Append( "// Compiled in Depth mode: Shade returns the alpha-tested coverage before lighting\n" );
		text.Append( Pixel( "DepthAlphaTestPs", true ) );
		text.Append( "#ifdef SURFACE_GPU_DRIVEN\n" );
		text.Append( GpuDrivenPixel( "ForwardPsGpuDriven", false, false ) );
		text.Append( GpuDrivenPixel( "ForwardAlphaTestPsGpuDriven", true, false ) );
		text.Append( GpuDrivenPixel( "ForwardTranslucentPsGpuDriven", false, false ) );
		text.Append( GpuDrivenPixel( "DepthAlphaTestPsGpuDriven", true, true ) );
		text.Append( "#endif\n" );
		text.Append( "#endif\n" );
		return text.ToString();
	}

	/// <summary>
	/// A pixel stage's feature constants: the flag that the pipeline is specialised for one material, each feature's value,
	/// and <c>SurfaceSpecialize</c>, which puts them in the record's place. Unspecialised, the record's values stand.
	/// </summary>
	static string Specialization( string type, IReadOnlyList<MaterialField> features )
	{
		var text = new StringBuilder();
		text.Append( "// The material's features as constants, when the pipeline is specialised for it\n" );
		text.Append( $"[vk::constant_id( {MaterialLayout.SpecializedId} )] const bool kSurfaceSpecialized = false;\n" );
		for ( int n = 0; n < features.Count; n++ ) text.Append( $"[vk::constant_id( {MaterialLayout.FirstFeatureId + n} )] const int kSurfaceFeature{n} = 0;\n" );
		text.Append( $"void SurfaceSpecialize( inout {type} m )\n{{\n\tif ( !kSurfaceSpecialized ) return;\n" );
		for ( int n = 0; n < features.Count; n++ )
		{
			var field = features[n];
			var value = field.Kind switch
			{
				MaterialFieldKind.Bool => $"kSurfaceFeature{n} != 0",
				MaterialFieldKind.UInt => $"uint( kSurfaceFeature{n} )",
				_ => $"kSurfaceFeature{n}",
			};
			text.Append( $"\tm.{field.Name} = {value};\n" );
		}

		text.Append( "}\n" );
		return text.ToString();
	}

	/// <summary>
	/// What a surface's vertex entries return: the engine's stage (moved by an <c>IVertexSurface</c>'s Displace) with the
	/// record offset, the <c>Varyings</c> too for an interpolated surface, which an <c>IInterpolatedVertexSurface</c>
	/// displaces with in hand.
	/// </summary>
	static string VertexStage( string type, bool displaces, bool interpolated )
	{
		if ( displaces && interpolated ) return $"SurfaceOutputDisplacedWith<{type}>( i, varyings )";
		var vertex = displaces ? $"SurfaceVertexDisplaced<{type}>( i )" : "SurfaceVertex( i )";
		return interpolated ? $"SurfaceOutputWith<{type}>( {vertex}, varyings )" : $"SurfaceOutput( {vertex} )";
	}

	/// <summary>Usages in the order GPU-driven geometry records number semantics (<c>usage * 16 + index</c>).</summary>
	static readonly string[] Usages = ["POSITION", "BLENDWEIGHT", "BLENDINDICES", "NORMAL", "PSIZE", "TEXCOORD", "TANGENT", "BINORMAL", "POSITIONT", "COLOR"];

	/// <summary>
	/// A surface's GPU-driven vertex entry (<c>MainVsGpuDriven</c>), generated from what its vertex stage reads
	/// (<paramref name="inputs"/>, from reflection): each input pulled from the draw's geometry by its semantic, the
	/// transform slot from the instance record, the tangent frame's encoding from the draw's flags, then the same stage.
	/// Appended to the instance file for a GPU-driven compile (<c>SURFACE_GPU_DRIVEN</c>).
	/// </summary>
	internal static string GpuDrivenVertex( string type, bool displaces, bool interpolated, IReadOnlyList<VertexInputSlot> inputs )
	{
		var text = new StringBuilder();
		text.Append( "\n#if ( PROGRAM == VFX_PROGRAM_VS )\n" );
		text.Append( "[shader( \"vertex\" )] SurfaceGpuDrivenVertexOutput MainVsGpuDriven( uint vertex : SV_VulkanVertexID, uint instance : SV_VulkanInstanceID )\n{\n" );
		text.Append( "\tuint2 record = BorealisGpuDrivenInstance( instance );\n" );
		text.Append( "\tuint4 draw = BorealisGpuDrivenDraw( record.y );\n" );
		text.Append( "\ts_SurfaceDrawRecord = draw.y;\n" );
		text.Append( "\tg_bUncompressedTangentFrame = ( draw.w & 1 ) != 0;\n" );
		text.Append( "\tSurfaceFetchBakedLighting( record.y );\n" );
		text.Append( "\tVertexInput i = {};\n" );
		if ( interpolated ) text.Append( $"\t{type}.Varyings varyings = {{}};\n" );
		foreach ( var input in inputs )
		{
			if ( input.Field is null ) throw new InvalidOperationException( $"vertex input {input.Semantic}{input.SemanticIndex} has no field" );
			if ( input.Semantic == "TEXCOORD" && input.SemanticIndex == 13 )
			{
				text.Append( $"\t{input.Field} = record.x;\n" );
				continue;
			}

			var usage = Array.IndexOf( Usages, input.Semantic );
			if ( usage < 0 || input.SemanticIndex >= 16 ) throw new InvalidOperationException( $"vertex input {input.Field} : {input.Semantic}{input.SemanticIndex} has no stream semantic" );
			var isFloat = input.Kind == ScalarKind.Float;
			var convert = isFloat ? "asfloat" : input.Kind == ScalarKind.Int ? "asint" : "";
			var swizzle = "xyzw"[..input.Components];
			text.Append( $"\t{input.Field} = {convert}( BorealisGpuDrivenFetch( draw.x, {usage * 16 + input.SemanticIndex}, vertex, {(isFloat ? "true" : "false")} ) ).{swizzle};\n" );
		}

		text.Append( "\tSurfaceGpuDrivenVertexOutput o;\n" );
		text.Append( $"\to.o = {VertexStage( type, displaces, interpolated )};\n" );
		text.Append( "\to.o.MaterialOffset = draw.z;\n" );
		text.Append( "\to.Draw = record.y;\n" );
		text.Append( "\to.VertexBit = BorealisGpuDrivenVertexBit( instance, vertex );\n" );
		text.Append( "\treturn o;\n}\n#endif\n" );
		return text.ToString();
	}
}
