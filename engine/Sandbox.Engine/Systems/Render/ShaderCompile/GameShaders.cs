using System.IO;
using System.Threading;

namespace Sandbox.Engine.Shaders;

/// <summary>
/// Compiles shaders for game code, behind <see cref="Shader.CompileAsync(string, string, CancellationToken)"/> and
/// <see cref="Shader.CompileFileAsync(BaseFileSystem, string, CancellationToken)"/>.
///
/// The source is copied, with the game's own includes expanded, into a cache folder game code can't write to, and
/// compiled there. Games can write their Data folder, so a .shader_c compiled next to the source could be swapped for
/// anything before it loads. A named shader is symlinked onto GAME under <c>compiled/</c>, so it never shadows a
/// packaged or core shader, and the links go when the game closes. An anonymous one is read back into memory and its
/// files deleted straight away.
/// </summary>
internal static class GameShaders
{
	const string ResourcePrefix = "compiled/";

	static readonly SemaphoreSlim gate = new( 1, 1 );
	static readonly HashSet<string> links = new( StringComparer.OrdinalIgnoreCase );
	static CancellationTokenSource session = new();
	static bool clearedCache;
	static int anonymousCount;

	static string CacheFolder => EngineFileSystem.Root.GetFullPath( "/.source2/shadercache" );

	/// <summary>
	/// Source from a string goes through a memory filesystem, so it's validated, cached and cleaned up the same way.
	/// Nothing relative to it exists, its includes come from GAME.
	/// </summary>
	internal static async Task<Shader> CompileSource( string name, string source, CancellationToken token )
	{
		ArgumentException.ThrowIfNullOrWhiteSpace( name );
		ArgumentNullException.ThrowIfNull( source );

		var path = name.Replace( '\\', '/' ).TrimStart( '/' );

		if ( !path.EndsWith( ".shader", StringComparison.OrdinalIgnoreCase ) )
			path += ".shader";

		if ( path.Split( '/' ).Contains( ".." ) )
			throw new ArgumentException( $"{name} can't use ..", nameof( name ) );

		var fileSystem = SourceFileSystem( path, source );

		try
		{
			return await Compile( fileSystem, path, token );
		}
		finally
		{
			fileSystem.Dispose();
		}
	}

	internal static async Task<Shader> Compile( BaseFileSystem fileSystem, string path, CancellationToken token )
	{
		ThreadSafe.AssertIsMainThread();
		ArgumentNullException.ThrowIfNull( fileSystem );
		ArgumentException.ThrowIfNullOrWhiteSpace( path );

		var relativePath = path.Replace( '\\', '/' ).TrimStart( '/' );

		if ( !relativePath.EndsWith( ".shader", StringComparison.OrdinalIgnoreCase ) )
			throw new ArgumentException( $"{path} isn't a .shader file", nameof( path ) );

		if ( relativePath.Split( '/' ).Contains( ".." ) )
			throw new ArgumentException( $"{path} can't use ..", nameof( path ) );

		if ( !fileSystem.FileExists( relativePath ) )
			throw new FileNotFoundException( $"Couldn't find {path}", path );

		var resourceName = (ResourcePrefix + relativePath).ToLowerInvariant();
		var cachePath = Path.Combine( CacheFolder, resourceName );

		var loadable = await Gated( token, async cancel =>
		{
			var result = await CompileInCache( fileSystem, relativePath, path, cachePath, cancel );

			// No renderer, no shaders: Shader.Load gives null for every shader on a dedicated server
			if ( !Graphics.IsAvailable )
				return false;

			if ( links.Add( resourceName ) )
			{
				NativeEngine.FullFileSystem.AddSymLink( resourceName + "_c", "GAME", cachePath + "_c" );
			}
			else if ( !result.Skipped )
			{
				// Not ConsoleSystem.Run, game code can't run native commands
				ConVarSystem.RunSingle( $"mat_reloadshaders \"{resourceName}\"" );
			}

			return true;
		} );

		return loadable ? Shader.Load( resourceName ) : null;
	}

	/// <summary>
	/// A shader nothing can find by name, like an anonymous texture or material. It lives in memory, and goes when
	/// nothing references it.
	/// </summary>
	internal static async Task<Shader> CompileAnonymous( string source, CancellationToken token )
	{
		ThreadSafe.AssertIsMainThread();
		ArgumentNullException.ThrowIfNull( source );

		var relativePath = $"anonymous/{Interlocked.Increment( ref anonymousCount )}.shader";
		var resourceName = ResourcePrefix + relativePath;
		var cachePath = Path.Combine( CacheFolder, resourceName );
		var fileSystem = SourceFileSystem( relativePath, source );

		byte[] compiled;

		try
		{
			compiled = await Gated( token, async cancel =>
			{
				try
				{
					var result = await CompileInCache( fileSystem, relativePath, relativePath, cachePath, cancel );
					return result.CompiledShader ?? await File.ReadAllBytesAsync( cachePath + "_c", cancel );
				}
				finally
				{
					File.Delete( cachePath );
					File.Delete( cachePath + "_c" );
				}
			} );
		}
		finally
		{
			fileSystem.Dispose();
		}

		if ( !Graphics.IsAvailable )
			return null;

		var shader = Shader.FromNative( CreateNative( resourceName, compiled ), resourceName );
		if ( shader is null )
			throw new ShaderCompileException( relativePath, ["Compiled, but the shader didn't load"] );

		shader.IsAnonymous = true;
		return shader;
	}

	static unsafe NativeEngine.CVfx CreateNative( string name, byte[] compiled )
	{
		fixed ( byte* data = compiled )
		{
			return NativeEngine.CVfx.CreateAnonymous( name, (IntPtr)data, compiled.Length );
		}
	}

	static BaseFileSystem SourceFileSystem( string path, string source )
	{
		var fileSystem = FileSystem.CreateMemoryFileSystem();

		var folder = Path.GetDirectoryName( path );
		if ( !string.IsNullOrEmpty( folder ) )
			fileSystem.CreateDirectory( folder.Replace( '\\', '/' ) );

		fileSystem.WriteAllText( path, source );
		return fileSystem;
	}

	/// <summary>
	/// One compile at a time, a compile already uses half the cores. Stops when the game closes.
	/// </summary>
	static async Task<T> Gated<T>( CancellationToken token, Func<CancellationToken, Task<T>> run )
	{
		using var cancel = CancellationTokenSource.CreateLinkedTokenSource( token, session.Token );
		await gate.WaitAsync( cancel.Token );

		try
		{
			// Whatever is left from the last run would load before it's compiled again
			if ( !clearedCache )
			{
				DeleteCache();
				clearedCache = true;
			}

			return await run( cancel.Token );
		}
		finally
		{
			gate.Release();
		}
	}

	/// <summary>
	/// Compile to <paramref name="cachePath"/>_c, throwing <see cref="ShaderCompileException"/> naming <paramref name="displayPath"/>
	/// </summary>
	static async Task<ShaderCompile.Results> CompileInCache( BaseFileSystem fileSystem, string relativePath, string displayPath, string cachePath, CancellationToken token )
	{
		// The native side only reads includes from GAME, which doesn't have the game's Data folder
		var preprocessor = new ShaderPreprocessor( new ShaderPreprocessorOptions { ExpandIncludes = true, IgnoreCoreIncludes = true, FileSystem = fileSystem } );
		var source = preprocessor.Preprocess( fileSystem.ReadAllText( relativePath ), cachePath, relativePath, addHeader: false );

		Directory.CreateDirectory( Path.GetDirectoryName( cachePath ) );
		await File.WriteAllTextAsync( cachePath, source, token );

		// Leave the game half the cores
		var options = new ShaderCompileOptions { MaxThreads = Math.Max( 1, ShaderCompile.ComboThreads( default ) / 2 ) };
		ShaderCompile.Results result;

		try
		{
			// Errors name the game's path, not the cache's
			result = await ShaderCompile.Compile( cachePath, relativePath, options, token );
		}
		catch ( Exception e ) when ( e is not OperationCanceledException )
		{
			// Source the compiler couldn't make sense of, like a program declared twice, or no vfx_vulkan.dll
			var reason = e is TypeInitializationException { InnerException: { } inner } ? inner : e;
			throw new ShaderCompileException( displayPath, [reason.Message] );
		}

		if ( !result.Success )
		{
			var errors = result.Programs.Where( x => !x.Success ).SelectMany( x => x.Output ?? [] ).ToList();

			// Failed before any program compiled, the reason is in the log
			if ( errors.Count == 0 )
				errors.Add( $"Couldn't read {displayPath}, check its blocks and includes. Includes are relative .hlsl, .fxc or .h files, without .." );

			throw new ShaderCompileException( displayPath, errors );
		}

		return result;
	}

	/// <summary>
	/// The game closed: stop its compiles and take its shaders off GAME
	/// </summary>
	internal static void Shutdown()
	{
		session.Cancel();
		session.Dispose();
		session = new();

		foreach ( var link in links )
		{
			NativeEngine.FullFileSystem.RemoveSymLink( link + "_c", "GAME" );
		}

		links.Clear();

		if ( clearedCache )
			DeleteCache();
	}

	static void DeleteCache()
	{
		try
		{
			if ( Directory.Exists( CacheFolder ) )
				Directory.Delete( CacheFolder, true );
		}
		catch ( IOException e )
		{
			Log.Warning( e, $"Couldn't clear {CacheFolder}" );
		}
	}
}
