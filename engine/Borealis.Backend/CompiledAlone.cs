using System.IO;
using Sandbox.ShaderCompiler;

namespace Borealis.Backend;

/// <summary>
/// Slang shaders shipped compiled alone: a package's <c>name.slang_c</c> with no <c>name.slang</c> beside it. They compile
/// from the source the compiled file embeds (<see cref="SlangOutput.SourceOf"/>), written out under
/// <c>game/.source2/borealisslangsource</c>: while the compiler that made the file is this one its entries are used as they
/// are, and when it isn't (an engine update) the text compiles again, cached per compiler. One that can't (compiled for a
/// shader API level this engine dropped, with no source, unreadable) is rejected once, by name, and its materials draw the
/// error shader. See docs/reference/shader-api.md.
/// </summary>
internal static class CompiledAlone
{
	static readonly HashSet<string> rejected = new( StringComparer.OrdinalIgnoreCase );

	/// <summary>
	/// The source to compile for the module at <paramref name="source"/> (<c>x.slang</c>) when only its compiled file
	/// (<c>x.slang_c</c>) is there, under <paramref name="game"/>'s cache; null when there's no compiled file either, or after
	/// telling <paramref name="error"/> once why it can't be used (<see cref="ShaderApi.Rejection"/>, naming
	/// <paramref name="package"/> when known).
	/// </summary>
	public static string SourceFor( string source, string game, Action<string> error, string package = null )
	{
		var compiled = SlangOutput.PathOf( Path.GetFullPath( source ) );
		if ( !File.Exists( compiled ) ) return null;

		string path, problem;
		try
		{
			path = SlangOutput.SourceOf( compiled, Path.Combine( game, ".source2", "borealisslangsource" ), out problem );
		}
		catch ( Exception e ) when ( e is IOException or UnauthorizedAccessException )
		{
			path = null;
			problem = $"its source couldn't be written out ({e.Message})";
		}

		if ( path is not null ) return path;
		lock ( rejected )
		{
			if ( !rejected.Add( compiled ) ) return null;
		}

		error?.Invoke( ShaderApi.Rejection( ShaderName( compiled ), package, problem ) );
		return null;
	}

	/// <summary>The name materials give the shader of a compiled file: <c>shaders/foo.shader</c> for <c>.../shaders/foo.slang_c</c> or <c>.../shaders/foo.shader_c</c>.</summary>
	internal static string ShaderName( string compiled )
	{
		var path = compiled.Replace( '\\', '/' );
		var at = path.LastIndexOf( "/shaders/", StringComparison.OrdinalIgnoreCase );
		var relative = at >= 0 ? path[(at + 1)..] : Path.GetFileName( path );
		return relative.EndsWith( SlangOutput.Suffix, StringComparison.OrdinalIgnoreCase ) ? ShaderFormat.ShaderNameOf( relative ) : relative + ".shader";
	}
}
