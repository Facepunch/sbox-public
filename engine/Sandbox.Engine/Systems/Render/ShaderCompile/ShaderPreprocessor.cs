using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace Sandbox.Engine.Shaders;

class ShaderPreprocessor
{
	ShaderPreprocessorOptions Options { get; set; }
	List<string> IncludedFiles = new();

	public ShaderPreprocessor( ShaderPreprocessorOptions options )
	{
		Options = options;
	}

	bool HandleInclude( string includeFile, string parentFile, out string includeContents )
	{
		includeContents = string.Empty;

		// Leave these for the native include handler, which refuses them
		if ( Path.IsPathRooted( includeFile ) || includeFile.Split( '/', '\\' ).Contains( ".." ) )
		{
			return false;
		}

		var fileSystem = Options.FileSystem ?? ShaderCompile.FileSystem;
		// Relative to the including file, then from the root, then in shaders/ - the same order as the native include
		// handler. Anything not expanded here is left out of the source embedded in the .shader_c, and won't be found
		// when a player's client recompiles it.
		string[] candidates =
		[
			$"{Path.GetDirectoryName( parentFile ).Replace( '\\', '/' )}/{includeFile}",
			includeFile,
			$"shaders/{includeFile}",
		];

		var path = candidates.FirstOrDefault( fileSystem.FileExists );
		if ( path is null )
		{
			return false;
		}

		bool coreContent = EngineFileSystem.CoreContent?.FileExists( path ) ?? false;
		if ( coreContent && Options.IgnoreCoreIncludes )
		{
			return false;
		}

		// Already included, handle it with an empty string out
		if ( IncludedFiles.Contains( path ) )
		{
			return true;
		}

		// Open the file somehow
		string source = fileSystem.ReadAllText( path );
		includeContents = Preprocess( source, fileSystem.GetFullPath( path ), path );

		IncludedFiles.Add( path );

		return true;
	}

	Regex IncludePattern = new Regex( "#include \"([^>]+)\"", RegexOptions.Compiled );

	// addHeader is false for a whole .shader file, where anything outside its blocks doesn't parse
	public string Preprocess( string source, string absolutePath, string relativePath, bool addHeader = true )
	{
		var reader = new StringReader( source );
		var builder = new StringBuilder();

		// Add a header to the source
		if ( addHeader )
		{
			builder.AppendLine( $"#line 1 \"{relativePath.Replace( "\\", "/" )}\"" );
			builder.AppendLine( "#include \"system.fxc\"" );
		}

		string line;
		while ( (line = reader.ReadLine()) != null )
		{
			var match = IncludePattern.Match( line );
			if ( !Options.ExpandIncludes || !match.Success )
			{
				builder = builder.AppendLine( line );
				continue;
			}

			var file = match.Groups[1].Value;

			if ( HandleInclude( file, relativePath, out var includeContents ) )
			{
				builder.AppendLine( includeContents );
				continue;
			}

			builder = builder.AppendLine( line );
		}

		return builder.ToString();
	}
}
