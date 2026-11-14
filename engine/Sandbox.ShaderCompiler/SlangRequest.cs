using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Sandbox.ShaderCompiler.Slang;

namespace Sandbox.ShaderCompiler;

/// <summary>
/// One Slang compile: a translation unit and its entry points, with vfx's options (vfx_vulkan's
/// <c>slang_shader_compiler.cpp</c>: entry point names kept, unscoped enums, scalar layout, column-major matrices, effect
/// annotations, uniformity validation, the same binding shifts and capabilities, Y inverted in vertex shaders). Every
/// file is read through <see cref="SlangFileSystem"/>, which applies the compiler's source rewrites.
/// </summary>
internal sealed unsafe class SlangRequest : IDisposable
{
	readonly IntPtr handle;
	readonly SlangFileSystem files;
	readonly Func<string, string, string> rewrite;
	int unit = -1;

	/// <summary>Annotations of every global the included files declared, by name.</summary>
	public Dictionary<string, Annotations> Annotations { get; } = new( StringComparer.Ordinal );

	/// <summary>
	/// A request on <paramref name="session"/> searching <paramref name="searchPaths"/>. <paramref name="rewrite"/> gets
	/// each file's path and text and returns what Slang compiles.
	/// </summary>
	public SlangRequest( IntPtr session, IReadOnlyList<string> searchPaths, IReadOnlyDictionary<string, string> defines, bool vertex, Func<string, string, string> rewrite )
	{
		this.rewrite = rewrite;
		handle = SlangApi.CreateCompileRequest( session );
		files = new SlangFileSystem( Load );
		SlangApi.SetFileSystem( handle, files.Pointer );

		var args = new List<string>
		{
			"-target", "spirv", "-profile", "spirv_1_5",
			"-fvk-use-entrypoint-name", "-unscoped-enum", "-force-glsl-scalar-layout", "-matrix-layout-column-major",
			"-enable-effect-annotations", "-validate-uniformity",
			// As vfx: implicit global, ?: deprecation, aliased bindless bindings, implicit conversion, unreachable code; and
			// undefined identifiers in #if, which vfx's own preprocessor accepts silently
			"-warnings-disable", "39019,30056,39001,30081,41000,15205",
			// vfx's binding shifts, the same in every set: b0 at 0, s0 at 14, t0 at 46, u0 at 174 (ShaderAbi)
			"-fvk-b-shift", "0", "all", "-fvk-s-shift", "14", "all", "-fvk-t-shift", "46", "all", "-fvk-u-shift", "174", "all",
		};

		foreach ( var capability in new[] { "spvDerivativeControl", "spvImageQuery", "spvImageGatherExtended", "spvSparseResidency", "spvMinLod", "spvGroupNonUniformArithmetic", "spvGroupNonUniformVote" } )
			args.AddRange( ["-capability", capability] );

		if ( vertex ) args.Add( "-fvk-invert-y" );
		foreach ( var path in searchPaths ) args.AddRange( ["-I", path] );
		foreach ( var (name, value) in defines ) args.Add( $"-D{name}={value}" );

		var encoded = args.Select( a => Marshal.StringToCoTaskMemUTF8( a ) ).ToArray();
		try
		{
			fixed ( IntPtr* pointers = encoded )
			{
				if ( SlangApi.ProcessCommandLineArguments( handle, (byte**)pointers, encoded.Length ) < 0 )
					throw new ShaderCompileException( null, $"Slang rejected the compile options: {Diagnostics}" );
			}
		}
		finally
		{
			foreach ( var p in encoded ) Marshal.FreeCoTaskMem( p );
		}
	}

	string Load( string path )
	{
		var full = Path.GetFullPath( path );
		if ( !File.Exists( full ) ) return null;
		return rewrite( full, File.ReadAllText( full ) );
	}

	/// <summary>Compile <paramref name="source"/> as the translation unit, reported as <paramref name="path"/>.</summary>
	public void AddSource( string path, string source )
	{
		var name = Encoding.UTF8.GetBytes( Path.GetFileNameWithoutExtension( path ) + "\0" );
		var pathBytes = Encoding.UTF8.GetBytes( path + "\0" );
		var sourceBytes = Encoding.UTF8.GetBytes( source + "\0" );
		fixed ( byte* n = name, p = pathBytes, s = sourceBytes )
		{
			unit = SlangApi.AddTranslationUnit( handle, SlangApi.LanguageSlang, n );
			SlangApi.AddTranslationUnitSourceString( handle, unit, p, s );
		}
	}

	/// <summary>Check and reflect only: no code (a module alone has nothing to emit).</summary>
	public void SkipCodegen() => SlangApi.SetCompileFlags( handle, SlangApi.CompileFlagNoCodegen );

	/// <summary>Add an entry point of the translation unit; its index.</summary>
	public int AddEntryPoint( string name, StageKind stage )
	{
		var bytes = Encoding.UTF8.GetBytes( name + "\0" );
		var kind = stage switch { StageKind.Vertex => SlangApi.StageVertex, StageKind.Compute => SlangApi.StageCompute, _ => SlangApi.StagePixel };
		fixed ( byte* n = bytes ) return SlangApi.AddEntryPoint( handle, unit, n, kind );
	}

	/// <summary>Slang's diagnostics so far.</summary>
	public string Diagnostics => SlangApi.String( SlangApi.GetDiagnosticOutput( handle ) ) ?? "";

	/// <summary>Compile; throws <see cref="ShaderCompileException"/> with Slang's diagnostics on failure.</summary>
	public void Compile( string path )
	{
		if ( SlangApi.Compile( handle ) < 0 ) throw new ShaderCompileException( path, Diagnostics );
	}

	/// <summary>
	/// The SPIR-V: the target's whole program, which is the one entry point each request adds (Slang emits SPIR-V for the
	/// whole program, so there's no per-entry code).
	/// </summary>
	public byte[] Code()
	{
		var blob = IntPtr.Zero;
		if ( SlangApi.GetTargetCodeBlob( handle, 0, &blob ) < 0 ) return null;
		return SlangApi.ReadBlob( blob );
	}

	/// <summary>The program's reflection, as Slang's JSON.</summary>
	public string ReflectionJson()
	{
		IntPtr blob;
		if ( SlangApi.ReflectionToJson( SlangApi.GetReflection( handle ), handle, &blob ) < 0 ) return null;
		var bytes = SlangApi.ReadBlob( blob );
		return bytes is null ? null : Encoding.UTF8.GetString( bytes );
	}

	/// <summary>The program's reflection handle, for <see cref="MaterialReflection"/>.</summary>
	public IntPtr Reflection => SlangApi.GetReflection( handle );

	public void Dispose()
	{
		SlangApi.DestroyCompileRequest( handle );
		files.Dispose();
	}
}

/// <summary>A shader that didn't compile, with the compiler's messages.</summary>
public sealed class ShaderCompileException( string path, string errors ) : Exception( $"{path}: {errors}" )
{
	/// <summary>The shader's source file.</summary>
	public string Path { get; } = path;

	/// <summary>Slang's diagnostics, or the compiler's own message.</summary>
	public string Errors { get; } = errors;
}
