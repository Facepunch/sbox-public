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

	/// <summary>Defines vfx gives every program, which the core includes test.</summary>
	static Dictionary<string, string> Defines( StageKind stage ) => new()
	{
		["SHADER_MODEL"] = "60",
		["PROGRAM"] = stage == StageKind.Vertex ? "1" : "2",
		["D3D"] = "1",
		["VULKAN"] = "1",
		["DXC"] = "1",
		["SLANG"] = "1",
	};

	/// <summary>What Slang compiles for each file: vfx's include rewrite for HLSL, the material rewrite for modules.</summary>
	static string Rewrite( string path, string text, Dictionary<string, Annotations> annotations )
	{
		if ( path.EndsWith( ".slang", StringComparison.OrdinalIgnoreCase ) ) return MaterialSource.Rewrite( text, out _ );
		if ( path.EndsWith( ".slangh", StringComparison.OrdinalIgnoreCase ) ) return text;
		return VfxIncludes.Rewrite( text, annotations );
	}

	SlangRequest Request( StageKind stage, IReadOnlyList<string> extraSearchPaths )
	{
		var session = SlangApi.Session( slangDirectory );
		Dictionary<string, Annotations> annotations = null;
		var request = new SlangRequest( session, [.. extraSearchPaths, .. searchPaths], Defines( stage ), stage == StageKind.Vertex, ( path, text ) => Rewrite( path, text, annotations ) );
		annotations = request.Annotations;
		return request;
	}

	/// <summary>
	/// Compile a surface: the module at <paramref name="path"/> with one <c>[Material]</c> struct implementing
	/// <c>ISurface</c>. Throws <see cref="ShaderCompileException"/> with Slang's messages on errors.
	/// </summary>
	public CompiledShader CompileSurface( string path )
	{
		path = Path.GetFullPath( path );
		var clock = Stopwatch.StartNew();
		var source = MaterialSource.Rewrite( File.ReadAllText( path ), out var declared );
		if ( declared.Count != 1 ) throw new ShaderCompileException( path, $"A surface module declares one [Material] struct; this one declares {declared.Count}" );

		var material = declared[0];
		var directory = Path.GetDirectoryName( path );
		var module = Path.GetFileNameWithoutExtension( path );
		if ( !ModuleName().IsMatch( module ) ) throw new ShaderCompileException( path, $"{module} isn't a valid module name: name surface files like identifiers" );

		lock ( compileLock )
		{
			var layout = ReflectMaterial( path, source, material.Name, directory );
			var displaces = material.Interfaces.Contains( "IVertexSurface" );
			var lit = material.Interfaces.Contains( "ILitSurface" );
			if ( !displaces && !lit && !material.Interfaces.Contains( "ISurface" ) )
				throw new ShaderCompileException( path, $"{material.Name} is a [Material] struct but implements none of ISurface, IVertexSurface or ILitSurface" );

			var instance = InstanceFile( module, material.Name, displaces, lit );
			var instancePath = Path.Combine( directory, $"{module}.instance.slang" );

			var vertex = CompileEntry( instancePath, instance, "MainVs", StageKind.Vertex, directory );
			var forward = CompileEntry( instancePath, instance, "ForwardPs", StageKind.Pixel, directory );
			var forwardAlphaTest = CompileEntry( instancePath, instance, "ForwardAlphaTestPs", StageKind.Pixel, directory );
			var depthAlphaTest = CompileEntry( instancePath, instance, "DepthAlphaTestPs", StageKind.Pixel, directory );

			return new CompiledShader
			{
				Name = material.Name,
				Tier = ShaderTier.Surface,
				Path = path,
				Material = layout,
				Passes = new Dictionary<string, CompiledPass>
				{
					[ShaderPasses.Forward] = new() { Name = ShaderPasses.Forward, Vertex = vertex, Pixel = forward },
					[ShaderPasses.Forward + "AlphaTest"] = new() { Name = ShaderPasses.Forward + "AlphaTest", Vertex = vertex, Pixel = forwardAlphaTest },
					[ShaderPasses.Depth] = new() { Name = ShaderPasses.Depth, Vertex = vertex },
					[ShaderPasses.DepthAlphaTest] = new() { Name = ShaderPasses.DepthAlphaTest, Vertex = vertex, Pixel = depthAlphaTest },
				},
				Generated = instance,
				Milliseconds = clock.Elapsed.TotalMilliseconds,
			};
		}
	}

	/// <summary>
	/// Compile a raw module: its author owns the entry points. <c>MainVs</c> and <c>MainPs</c> make the forward pass,
	/// <c>MainVs</c> alone the depth pass, and <c>DepthPs</c>, when there is one, the alpha-tested depth pass. Its
	/// parameters are a <c>[Material]</c> struct read with <c>LoadMaterial&lt;T&gt;()</c>, as surfaces' are.
	/// </summary>
	public CompiledShader CompileRaw( string path )
	{
		path = Path.GetFullPath( path );
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
			};
		}
	}

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

	CompiledStage CompileEntry( string path, string source, string entry, StageKind stage, string directory )
	{
		using var request = Request( stage, [directory] );
		request.AddSource( path, source );
		request.AddEntryPoint( entry, stage );
		request.Compile( path );

		var spirv = request.Code() ?? throw new ShaderCompileException( path, $"{entry}: Slang returned no code. {request.Diagnostics}" );

		// The pixel stage's own resources go in set 1, as vfx puts them, so the two stages' bindings never collide
		if ( stage == StageKind.Pixel ) spirv = Spirv.MoveSet( spirv, 0, 1 );

		return StageReflection.Describe( stage, entry, spirv, request.ReflectionJson(), request.Annotations );
	}

	/// <summary>The instance file for a <c>[Material]</c> struct: the engine's passes, named as entry points.</summary>
	internal static string InstanceFile( string module, string type, bool displaces, bool lit )
	{
		var forward = lit ? "SurfaceForwardLit" : "SurfaceForward";
		var vertex = displaces ? $"SurfaceVertexDisplaced<{type}>( i )" : "SurfaceVertex( i )";
		var text = new StringBuilder();
		text.Append( "// Generated by Sandbox.ShaderCompiler for the [Material] struct " ).Append( type ).Append( " in " ).Append( module ).Append( ".slang.\n" );
		text.Append( "#include \"sbox/surface_passes.slangh\"\n" );
		text.Append( "import " ).Append( module ).Append( ";\n\n" );
		text.Append( "#if ( PROGRAM == VFX_PROGRAM_VS )\n" );
		text.Append( "[shader( \"vertex\" )] SurfaceVertexOutput MainVs( VertexInput i ) { return SurfaceOutput( " ).Append( vertex ).Append( " ); }\n" );
		text.Append( "#else\n" );
		text.Append( "[shader( \"pixel\" )] float4 ForwardPs( SurfaceVertexOutput i ) : SV_Target0 { return " ).Append( forward ).Append( '<' ).Append( type ).Append( ", false>( i.Pixel, PixelMaterialOffset( i.MaterialOffset ) ); }\n" );
		text.Append( "[shader( \"pixel\" )] float4 ForwardAlphaTestPs( SurfaceVertexOutput i ) : SV_Target0 { return " ).Append( forward ).Append( '<' ).Append( type ).Append( ", true>( i.Pixel, PixelMaterialOffset( i.MaterialOffset ) ); }\n" );
		text.Append( "[shader( \"pixel\" )] void DepthAlphaTestPs( SurfaceVertexOutput i ) { SurfaceDepth<" ).Append( type ).Append( ">( i.Pixel, PixelMaterialOffset( i.MaterialOffset ) ); }\n" );
		text.Append( "#endif\n" );
		return text.ToString();
	}
}
