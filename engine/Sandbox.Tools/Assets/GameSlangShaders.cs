using System;
using Borealis.Backend;
using Sandbox.ShaderCompiler;
using System.IO;
using System.Threading;

namespace Editor;

/// <summary>
/// A game project's own Slang shaders (<c>.slang</c> surfaces, raw modules and pass modules under its assets), compiled
/// offline into <c>name.slang_c</c> beside each (<see cref="OfflineShaders.CompileGameShaderAsync"/>) when the project
/// loads, when one of them (or a header they include) is saved, and before publishing, which ships them with their
/// sources. Players' devices read the offline compile instead of compiling the shader on its first draw.
/// </summary>
internal static class GameSlangShaders
{
	static Task running;
	static bool again;

	static string GameFolder => Path.GetFullPath( EngineFileSystem.Root.GetFullPath( "/" ) );

	/// <summary>The asset folders of the active projects (the game and its libraries).</summary>
	static IEnumerable<string> Folders() => Project.All.Where( p => p.Active && p.HasAssetsPath() ).Select( p => Path.GetFullPath( p.GetAssetsPath() ) ).Distinct( StringComparer.OrdinalIgnoreCase );

	/// <summary>Whether <paramref name="file"/> is Slang source (<c>.slang</c> or <c>.slangh</c>), not an offline compile.</summary>
	internal static bool IsSource( string file ) =>
		file.EndsWith( ".slang", StringComparison.OrdinalIgnoreCase ) || file.EndsWith( ".slangh", StringComparison.OrdinalIgnoreCase );

	/// <summary>
	/// A content file changed: when it's a project's Slang source, compile that project's Slang again (what didn't change is
	/// current and skipped). A change during a compile starts another after it.
	/// </summary>
	internal static void Changed( string filename )
	{
		if ( !IsSource( filename ) ) return;
		var full = FileSystem.Content.GetFullPath( filename );
		if ( full is null || !Folders().Any( f => Path.GetFullPath( full ).StartsWith( f + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase ) ) ) return;

		if ( running is { IsCompleted: false } )
		{
			again = true;
			return;
		}

		running = CompileAsync();
	}

	/// <summary>Compile every active project's Slang shaders whose offline compile is missing or out of date; how many failed.</summary>
	internal static async Task<int> CompileAsync( CancellationToken cancel = default )
	{
		var failed = 0;
		do
		{
			again = false;
			failed = 0;
			foreach ( var folder in Folders() )
			{
				failed += await Task.Run( () => OfflineShaders.CompileGameShadersAsync( GameFolder, folder, Report, cancel ), cancel );
			}
		}
		while ( again && !cancel.IsCancellationRequested );

		return failed;
	}

	static void Report( string message )
	{
		if ( message.EndsWith( $": {OfflineResult.Compiled}", StringComparison.Ordinal ) ) Log.Info( message );
		else Log.Warning( message );
	}

	/// <summary>A project folder's Slang files to ship: sources, headers and their offline compiles, as (absolute, relative) paths.</summary>
	internal static IEnumerable<(string Absolute, string Relative)> PublishFiles( string folder )
	{
		if ( !Directory.Exists( folder ) ) yield break;
		foreach ( var file in Directory.EnumerateFiles( folder, "*", SearchOption.AllDirectories ) )
		{
			if ( !IsSource( file ) && !file.EndsWith( ".slang" + SlangOutput.Suffix, StringComparison.OrdinalIgnoreCase ) ) continue;
			yield return (file, Path.GetRelativePath( folder, file ).Replace( '\\', '/' ));
		}
	}
}
