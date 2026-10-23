namespace Sandbox.Engine.Shaders;

/// <summary>
/// Recompiles .shader files when they change on disk and reloads them, for running outside the editor, where
/// the asset system isn't there to do it. Only the .shader itself is watched, editing an include won't recompile
/// the shaders that use it.
/// </summary>
internal static class ShaderHotload
{
	static readonly Logger log = new( "ShaderHotload" );

	static readonly List<(BaseFileSystem FileSystem, string Path)> queue = new();
	static bool compiling;

	internal static FileWatch Watch( BaseFileSystem fileSystem )
	{
		var watcher = fileSystem.Watch( "*.shader" );
		watcher.OnChangedFile += file => Enqueue( fileSystem, file );
		return watcher;
	}

	static void Enqueue( BaseFileSystem fileSystem, string path )
	{
		if ( !fileSystem.FileExists( path ) )
			return;

		if ( !queue.Contains( (fileSystem, path) ) )
			queue.Add( (fileSystem, path) );

		if ( !compiling )
			_ = CompileQueue();
	}

	/// <summary>
	/// One at a time, a compile already uses every core
	/// </summary>
	static async Task CompileQueue()
	{
		compiling = true;

		try
		{
			while ( queue.Count > 0 )
			{
				var (fileSystem, path) = queue[0];
				queue.RemoveAt( 0 );

				await Compile( fileSystem, path );
			}
		}
		finally
		{
			compiling = false;
		}
	}

	static async Task Compile( BaseFileSystem fileSystem, string path )
	{
		var relativePath = path.TrimStart( '/' );
		var absolutePath = fileSystem.GetFullPath( path );

		try
		{
			var timer = FastTimer.StartNew();
			var result = await ShaderCompile.Compile( absolutePath, relativePath, new ShaderCompileOptions(), default );

			if ( !result.Success )
			{
				foreach ( var line in result.Programs.Where( x => !x.Success ).SelectMany( x => x.Output ?? [] ) )
				{
					log.Warning( line );
				}

				log.Warning( $"Failed to compile {relativePath}" );
				return;
			}

			if ( result.Skipped )
				return;

			// Not ConsoleSystem.Run, game code can't run native commands
			ConVarSystem.RunSingle( $"mat_reloadshaders \"{relativePath}\"" );
			log.Info( $"Compiled {relativePath} in {timer.ElapsedMilliSeconds:n0}ms" );
		}
		catch ( Exception e )
		{
			log.Warning( e, $"Failed to compile {relativePath}" );
		}
	}
}
