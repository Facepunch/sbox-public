using System.Text;

namespace Facepunch.Native;

/// <summary>
/// Unity builds: batches of sources are compiled as one translation unit, which is a lot faster on a
/// full build. Only plain sources take part, so anything with its own compile settings still builds on
/// its own and can be edited without recompiling its neighbours.
/// </summary>
public static class Unity
{
	public const int BatchSize = 20;
	public const string Folder = "obj/unity";

	public static void Apply( Module module )
	{
		if ( !module.Unity ) return;

		Schema( module );
		Sources( module );
	}

	private static void Sources( Module module )
	{
		var configs = module.Configs.Select( c => c.Name ).ToList();
		int index = 0;

		// Grouped by directory: files that live together were written to live together, and merging across
		// a whole module runs into duplicate file local names.
		var folders = module.ResolvedFiles
			.Where( f => Eligible( module, f ) )
			.GroupBy( f => Path.GetDirectoryName( f.Path )?.Replace( '\\', '/' ) ?? "", StringComparer.OrdinalIgnoreCase )
			.OrderBy( g => g.Key, StringComparer.OrdinalIgnoreCase );

		foreach ( var folder in folders )
		{
			var sources = folder.OrderBy( f => f.Path, StringComparer.OrdinalIgnoreCase ).ToList();
			if ( sources.Count < 2 ) continue;

			foreach ( var batch in sources.Chunk( BatchSize ) )
			{
				var path = $"{module.Dir}/{Folder}/unity_{++index:00}.cpp";
				var content = new StringBuilder( Header );

				foreach ( var source in batch )
					content.Append( $"#include \"{Relative( path, source.Path )}\"\r\n" );

				WriteFile( path, content.ToString() );

				// The sources stay in the project so they are still browsable and searchable.
				foreach ( var source in batch ) source.ExcludeFrom = [.. configs];

				module.ResolvedFiles.Add( new SourceFile { Path = path, Kind = FileKind.Compile } );
			}
		}
	}

	/// <summary>
	/// Schema generated code gets its own batches, with the configuration chosen inside the file. The
	/// generated files come in a pair per configuration, and expect to be compiled next to each other.
	/// </summary>
	private static void Schema( Module module )
	{
		var generated = module.ResolvedFiles
			.Where( f => f.Kind == FileKind.Compile && f.Path.Contains( SchemaCompiler.GeneratedRoot ) && f.ExcludeFrom.Count > 0 )
			.ToList();

		if ( generated.Count == 0 ) return;

		var pairs = generated
			.GroupBy( f => Path.GetFileName( f.Path ), StringComparer.OrdinalIgnoreCase )
			.OrderBy( g => g.Key, StringComparer.OrdinalIgnoreCase )
			.ToList();

		int index = 0;

		foreach ( var batch in pairs.Chunk( BatchSize ) )
		{
			var path = $"{module.Dir}/{Folder}/schema_{++index:00}.cpp";
			var content = new StringBuilder( Header );

			foreach ( var pair in batch )
			{
				var debug = pair.FirstOrDefault( f => !f.ExcludeFrom.Contains( "Debug" ) );
				var release = pair.FirstOrDefault( f => !f.ExcludeFrom.Contains( "Release" ) );
				if ( debug is null || release is null ) continue;

				content.Append( "#if defined( _DEBUG )\r\n" );
				content.Append( $"#include \"{Relative( path, debug.Path )}\"\r\n" );
				content.Append( "#else\r\n" );
				content.Append( $"#include \"{Relative( path, release.Path )}\"\r\n" );
				content.Append( "#endif\r\n" );
			}

			WriteFile( path, content.ToString() );

			foreach ( var file in batch.SelectMany( x => x ) ) module.ResolvedFiles.Remove( file );
			module.ResolvedFiles.Add( new SourceFile { Path = path, Kind = FileKind.Compile } );
		}
	}

	/// <summary>Files with their own compile settings are left out; merging them would change how they build.</summary>
	private static bool Eligible( Module module, SourceFile file ) =>
		!module.Matchers( module.NoUnityFiles ).Any( m => m( file.Path ) )
		&& file.Kind == FileKind.Compile
		&& file.Build is null
		&& !file.NoPch
		&& !file.CreatePch
		&& file.CompileAs is null
		&& file.Msvc.Count == 0 && file.Clang.Count == 0
		&& file.Defines.Count == 0
		&& file.Includes.Count == 0
		&& file.ExcludeFrom.Count == 0
		&& file.Path.EndsWith( ".cpp", StringComparison.OrdinalIgnoreCase )
		&& !file.Path.Contains( SchemaCompiler.GeneratedRoot );

	private const string Header = "// Generated unity file. Edit the module definition, not this.\r\n\r\n";

	private static string Relative( string unityFile, string source )
	{
		var from = Path.GetDirectoryName( Path.Combine( Paths.SrcDir, unityFile ) );
		return Path.GetRelativePath( from, Path.Combine( Paths.SrcDir, source ) ).Replace( '\\', '/' );
	}

	private static void WriteFile( string path, string text )
	{
		var full = Path.Combine( Paths.SrcDir, path );
		if ( File.Exists( full ) && File.ReadAllText( full ) == text ) return;

		Directory.CreateDirectory( Path.GetDirectoryName( full ) );
		File.WriteAllText( full, text, new UTF8Encoding( false ) );
	}
}
