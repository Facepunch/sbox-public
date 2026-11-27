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

	/// <summary>
	/// Whether the file at <paramref name="file"/> is Slang source (<c>.slang</c>, <c>.slangh</c>, or a <c>.shader</c> that's
	/// Slang by content: <see cref="ShaderFormat"/>), not an offline compile.
	/// </summary>
	internal static bool IsSource( string file ) => ShaderFormat.IsSlangSourcePath( file );

	/// <summary>
	/// A content file changed: when it's a project's Slang source, compile that project's Slang again (what didn't change is
	/// current and skipped). A change during a compile starts another after it.
	/// </summary>
	internal static void Changed( string filename )
	{
		// A .shader is told by content, so by its file once it's known to be a project's
		var shader = filename.EndsWith( ".shader", StringComparison.OrdinalIgnoreCase );
		if ( !shader && !IsSource( filename ) ) return;
		var full = FileSystem.Content.GetFullPath( filename );
		if ( full is null || !Folders().Any( f => Path.GetFullPath( full ).StartsWith( f + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase ) ) ) return;
		if ( shader && !ShaderFormat.IsSlangSourceFile( full ) ) return;

		if ( running is { IsCompleted: false } )
		{
			again = true;
			return;
		}

		running = CompileAndDescribeAsync();
	}

	static readonly Dictionary<string, string> described = new( StringComparer.OrdinalIgnoreCase );

	/// <summary>
	/// <see cref="CompileAsync"/>, then game code compiled again for each project whose surfaces' descriptions changed, so
	/// its typed classes (Sandbox.Generator's SurfaceClassWriter) follow the shaders.
	/// </summary>
	static async Task CompileAndDescribeAsync()
	{
		await CompileAsync();
		await DescribeAsync( recompile: true );
	}

	/// <summary>As the project loads: its Slang compiled, and what its surfaces' classes are made from noted.</summary>
	internal static async Task StartupAsync()
	{
		OfflineShaders.GameWarning ??= message => Log.Warning( message );
		await CompileAsync();
		await DescribeAsync( recompile: false );
	}

	static async Task DescribeAsync( bool recompile )
	{
		foreach ( var project in Project.All.Where( p => p.Active && p.HasAssetsPath() && p.Compiler is not null ) )
		{
			var folder = Path.GetFullPath( project.GetAssetsPath() );
			var now = await Task.Run( () => string.Join( "\n", OfflineShaders.DescribeGameSurfaces( GameFolder, folder ).Select( d => d.Path + "\n" + d.Description ) ) );
			var before = described.GetValueOrDefault( folder );
			described[folder] = now;
			if ( recompile && before != now ) project.Compiler.MarkForRecompile();
		}
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
			if ( !IsSource( file ) && !ShaderFormat.IsSlangCompiledPath( file ) ) continue;
			yield return (file, Path.GetRelativePath( folder, file ).Replace( '\\', '/' ));
		}
	}
}
