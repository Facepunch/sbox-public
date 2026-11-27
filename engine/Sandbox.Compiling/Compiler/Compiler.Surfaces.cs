using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace Sandbox;

partial class Compiler
{
	/// <summary>
	/// Files the archive carries beside the code, asked for each build from the file system (not from an archive): the
	/// project's Slang surfaces' descriptions (<see cref="Generator.SurfaceClassWriter.Extension"/>), which the engine
	/// reflects from its shaders, so game code gets a typed class per surface. On a worker thread.
	/// </summary>
	internal Func<IEnumerable<CodeArchive.AdditionalFile>> AdditionalFilesProvider { get; set; }

	/// <summary>The provider's files, into an archive being built.</summary>
	void AddProvidedFiles( CodeArchive archive )
	{
		if ( AdditionalFilesProvider is not { } provider ) return;

		IEnumerable<CodeArchive.AdditionalFile> files;
		try
		{
			files = provider()?.ToList() ?? [];
		}
		catch ( Exception e )
		{
			log.Warning( e, $"Couldn't describe {Name}'s shaders for their classes" );
			return;
		}

		foreach ( var file in files )
		{
			if ( file?.Text is null || string.IsNullOrEmpty( file.LocalPath ) ) continue;
			archive.AdditionalFiles.Add( file );
			archive.FileHashMap[file.LocalPath] = file.Text.FastHash64();
		}
	}

	/// <summary>
	/// The typed class of each surface description in the archive (<see cref="Generator.SurfaceClassWriter"/>), in the
	/// project's root namespace's <c>Surfaces</c>. A description this engine doesn't read is left out.
	/// </summary>
	List<SyntaxTree> ProcessSurfaceFiles( CodeArchive archive )
	{
		var trees = new List<SyntaxTree>();
		var parseOptions = archive.Configuration.GetParseOptions();
		var seen = new HashSet<string>( StringComparer.OrdinalIgnoreCase );
		foreach ( var file in archive.AdditionalFiles.OrderBy( f => f.LocalPath, StringComparer.Ordinal ) )
		{
			if ( !file.LocalPath.EndsWith( Generator.SurfaceClassWriter.Extension, StringComparison.OrdinalIgnoreCase ) ) continue;
			if ( Generator.SurfaceClassWriter.Write( file.Text, archive.Configuration.RootNamespace ) is not { } code ) continue;

			var path = $"_gen_surface_{System.IO.Path.GetFileNameWithoutExtension( file.LocalPath )}_{file.LocalPath.FastHash():x}.cs";
			if ( !seen.Add( path ) ) continue;
			trees.Add( CSharpSyntaxTree.ParseText( SourceText.From( code, System.Text.Encoding.UTF8 ), parseOptions, path ) );
		}

		return trees;
	}
}
