using Borealis.Source2;
using Sandbox.ShaderCompiler;
using System.IO;

namespace Sandbox.BorealisLab;

/// <summary>
/// <c>-convert-local folder -local-roots "a;b"</c>: every VFX <c>.shader</c> under the roots (a project's or addon's, told by
/// content: <see cref="ShaderFormat.IsVfxSource"/>) made into a standalone module (<see cref="PassConverter.ConvertStandalone"/>)
/// and compiled, each stage with every combo at its first value and each at its last in turn, against the engine's includes,
/// the shader's folder and its project's <c>Assets</c> folder: what compiling local shaders without vfx would make of them.
/// Writes each module and <c>local.tsv</c> into the folder. No device and nothing of the engine starts.
/// </summary>
internal static class LocalShaders
{
	public static bool Requested => Args.String( "-convert-local" ) is not null;

	public static int Run()
	{
		var folder = Path.GetFullPath( Args.String( "-convert-local" ) );
		Directory.CreateDirectory( folder );
		var core = ContentFiles.FindGame().Roots.First( r => File.Exists( Path.Combine( r, "shaders", "sbox", "pass.slangh" ) ) );
		var shaders = Path.Combine( core, "shaders" );
		var compiler = new SlangCompiler( Borealis.Backend.EngineFolders.NativeIn( Path.GetDirectoryName( core ) ), [shaders] ) { CacheFolder = Path.Combine( folder, "cache" ) };

		var lines = new List<string> { "path\tresult" };
		var compiled = 0;
		var files = (Args.String( "-local-roots" ) ?? "").Split( ';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries )
			.SelectMany( r => File.Exists( r ) ? (IEnumerable<string>)[r] : Directory.EnumerateFiles( r, "*.shader", SearchOption.AllDirectories ) ).Order( StringComparer.OrdinalIgnoreCase ).ToList();
		foreach ( var file in files )
		{
			var text = File.ReadAllText( file );
			string result;
			if ( !ShaderFormat.IsVfxSource( text ) ) result = "not a VFX source";
			else if ( PassConverter.Refuse( text ) is { } why ) result = "refused: " + why;
			else
			{
				var stem = Path.GetFileNameWithoutExtension( file );
				var module = PassConverter.ConvertStandalone( text, stem );
				var path = Path.Combine( folder, stem + ".slang" );
				File.WriteAllText( path, module );
				var assets = Ancestor( file, "Assets" );
				var errors = PassConversion.Compile( compiler, PassModule.FromText( path, module, assets is null ? [shaders, Path.GetDirectoryName( file )] : [shaders, Path.GetDirectoryName( file ), assets] ), out var stages );
				if ( errors is null ) compiled++;
				else File.WriteAllText( Path.ChangeExtension( path, ".errors.txt" ), errors );
				result = errors is null ? $"compiles ({stages})" : "fails: " + (errors.Split( '\n' ).FirstOrDefault( l => l.Contains( "error", StringComparison.OrdinalIgnoreCase ) ) ?? "").Trim();
			}

			Console.WriteLine( $"{Path.GetFileName( file )}\t{result}" );
			lines.Add( $"{file}\t{result.Replace( '\t', ' ' )}" );
		}

		File.WriteAllLines( Path.Combine( folder, "local.tsv" ), lines );
		Console.WriteLine( $"Local shaders: {compiled} of {files.Count} compile" );
		return 0;
	}

	static string Ancestor( string file, string name )
	{
		for ( var directory = Path.GetDirectoryName( file ); directory is not null; directory = Path.GetDirectoryName( directory ) )
		{
			if ( string.Equals( Path.GetFileName( directory ), name, StringComparison.OrdinalIgnoreCase ) ) return directory;
		}

		return null;
	}
}
