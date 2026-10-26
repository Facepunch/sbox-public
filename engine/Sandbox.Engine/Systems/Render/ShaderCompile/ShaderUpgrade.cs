using Humanizer;
using Sandbox.Internal;
using System.Buffers.Binary;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;

namespace Sandbox.Engine.Shaders;

/// <summary>
/// Brings the outdated .shader_c files of a downloaded package up to date as it installs, before anything loads them.
/// A shader compiled by an older engine (an older resource version or ABI epoch) won't load, so it's recompiled from
/// the source embedded in it, against the engine's shader includes as they are now. The result is cached by the
/// original's CRC and the versions it was compiled for, in a folder game code can't write, and the package's file is
/// pointed at it.
/// </summary>
internal static class ShaderUpgrade
{
	/// <summary>
	/// What a compiled shader has to be stamped with to load
	/// </summary>
	internal readonly record struct Versions( int Resource, int MinVcs, int Vcs, int Abi )
	{
		internal static Versions Current => new( ShaderTools.GetResourceVersion(), ShaderTools.GetMinVcsVersion(), ShaderTools.GetVcsVersion(), ShaderTools.GetAbiVersion() );

		public override string ToString() => $"r{Resource}v{Vcs}a{Abi}";
	}

	internal enum State
	{
		/// <summary>Loads as it is</summary>
		Current,

		/// <summary>Won't load, but can be read to recompile</summary>
		Outdated,

		/// <summary>Can't be read at all: not a compiled shader, or a VCS version this engine doesn't read</summary>
		Unreadable,
	}

	const uint RESOURCE_BLOCK_ID_DATA = 0x41544144; // 'DATA'
	const ushort RESOURCE_FILE_HEADER_VERSION = 12;
	const uint VcsFileFormatId = 0x32736376; // 'vcs2'

	static string CacheFolder => EngineFileSystem.Root.GetFullPath( "/.source2/shaderupgrade" );
	static bool pruned;

	// A compile already uses every core, and packages install in parallel
	static readonly SemaphoreSlim gate = new( 1, 1 );

	/// <summary>
	/// Whether a compiled shader loads, reading only its headers: the resource version, then the VCS version and ABI
	/// epoch at the start of each program. Mirrors CShaderTypeManager and CVfx::CreateFromResourceFile.
	/// </summary>
	internal static State GetState( Stream stream, Versions current )
	{
		Span<byte> header = stackalloc byte[16];
		if ( !TryRead( stream, 0, header ) )
			return State.Unreadable;

		if ( BinaryPrimitives.ReadUInt16LittleEndian( header[4..] ) != RESOURCE_FILE_HEADER_VERSION )
			return State.Unreadable;

		var resourceVersion = BinaryPrimitives.ReadUInt16LittleEndian( header[6..] );
		var blocksOffset = BinaryPrimitives.ReadInt32LittleEndian( header[8..] );
		var blockCount = BinaryPrimitives.ReadUInt32LittleEndian( header[12..] );

		if ( blockCount > 64 )
			return State.Unreadable;

		// The programs are found through DATA, which is empty and lands on SPRV
		long dataPosition = -1;
		Span<byte> entry = stackalloc byte[12];

		for ( var i = 0; i < blockCount; i++ )
		{
			var entryPosition = 8L + blocksOffset + i * 12;
			if ( !TryRead( stream, entryPosition, entry ) )
				return State.Unreadable;

			if ( BinaryPrimitives.ReadUInt32LittleEndian( entry ) != RESOURCE_BLOCK_ID_DATA )
				continue;

			dataPosition = entryPosition + 4 + BinaryPrimitives.ReadInt32LittleEndian( entry[4..] );
			break;
		}

		if ( dataPosition < 0 )
			return State.Unreadable;

		// ShaderHeader_t: an offset and size per program, offsets relative to DATA, 0 for programs it doesn't have
		var programCount = (int)ShaderProgramType.VFX_PROGRAM_MAX;
		Span<byte> programs = stackalloc byte[programCount * 8];
		if ( !TryRead( stream, dataPosition, programs ) )
			return State.Unreadable;

		var outdated = resourceVersion < current.Resource;
		var found = false;
		Span<byte> vcs = stackalloc byte[12];

		for ( var i = 0; i < programCount; i++ )
		{
			var offset = BinaryPrimitives.ReadUInt32LittleEndian( programs[(i * 8)..] );
			if ( offset == 0 )
				continue;

			if ( !TryRead( stream, dataPosition + offset, vcs ) || BinaryPrimitives.ReadUInt32LittleEndian( vcs ) != VcsFileFormatId )
				return State.Unreadable;

			var vcsVersion = BinaryPrimitives.ReadInt32LittleEndian( vcs[4..] );
			if ( vcsVersion < current.MinVcs || vcsVersion > current.Vcs )
				return State.Unreadable;

			if ( BinaryPrimitives.ReadInt32LittleEndian( vcs[8..] ) != current.Abi )
				outdated = true;

			found = true;
		}

		if ( !found )
			return State.Unreadable;

		return outdated ? State.Outdated : State.Current;
	}

	static bool TryRead( Stream stream, long position, Span<byte> buffer )
	{
		if ( position < 0 || position + buffer.Length > stream.Length )
			return false;

		stream.Position = position;
		stream.ReadExactly( buffer );
		return true;
	}

	static State GetState( string path, Versions current )
	{
		try
		{
			using var stream = File.OpenRead( path );
			return GetState( stream, current );
		}
		catch ( IOException )
		{
			return State.Unreadable;
		}
	}

	/// <summary>
	/// Where the recompiled copy of a shader goes, by its path, its CRC and what it's compiled for
	/// </summary>
	internal static string CacheName( string path, ulong crc, Versions versions )
	{
		var name = path.Replace( '\\', '/' ).TrimStart( '/' );
		if ( name.EndsWith( ".shader_c", StringComparison.OrdinalIgnoreCase ) )
			name = name[..^".shader_c".Length];

		name = Regex.Replace( name, "[^a-zA-Z0-9/]", "_" );
		return $"{name}.{crc:x}.{versions}.shader_c";
	}

	record struct Candidate( string Path, string AbsolutePath );

	/// <summary>
	/// Recompile a downloaded package's outdated shaders and point it at the new ones. Run it before the package mounts:
	/// native keeps a link's first target while anything references it. Shaders that can't be recompiled stay as they
	/// are, and don't load, the same as before.
	/// </summary>
	internal static async Task UpgradeAsync( RedirectFileSystem fileSystem, string packageTitle, CancellationToken token )
	{
		// A dedicated server doesn't render, it never loads them
		if ( fileSystem is null || !Graphics.IsAvailable )
			return;

		var shaders = fileSystem.Files.Keys
			.Select( x => x.FullName )
			.Where( x => x.EndsWith( ".shader_c", StringComparison.OrdinalIgnoreCase ) )
			// Core shaders win over packaged ones, AddSymLink never links them
			.Where( x => !EngineFileSystem.CoreContent.FileExists( x ) )
			.Select( x => new Candidate( x, fileSystem.ConvertPathToInternal( x ) ) )
			.ToArray();

		if ( shaders.Length == 0 )
			return;

		var current = Versions.Current;

		var outdated = await Task.Run( () => shaders.Where( x => GetState( x.AbsolutePath, current ) == State.Outdated ).ToArray(), token );
		if ( outdated.Length == 0 )
			return;

		// Compiling goes through native shader objects
		await MainThread.Wait();
		await gate.WaitAsync( token );

		var title = LoadingScreen.Title;

		try
		{
			PruneCache( current );

			// Ones already recompiled, or that failed on this build, are settled without compiling anything
			var jobs = await Task.Run( () => outdated.Select( x => GetJob( x, current ) ).ToArray(), token );
			var compiling = new List<Job>();
			var failed = 0;

			foreach ( var job in jobs )
			{
				if ( job.Cached )
				{
					fileSystem.Repoint( job.Shader.Path, job.CachePath );
				}
				else if ( job.FailedBefore is { } reason )
				{
					Log.Warning( $"{job.Shader.Path} is outdated and didn't recompile last time, it won't load{(reason is "" ? "" : $": {reason}")}" );
					failed++;
				}
				else
				{
					compiling.Add( job );
				}
			}

			if ( compiling.Count == 0 )
				return;

			Log.Info( $"{packageTitle}: {compiling.Count} outdated {(compiling.Count == 1 ? "shader" : "shaders")} to update" );

			LoadingScreen.Subtitle = null;
			LoadingScreen.StepFraction = 0;

			// The bar goes by combos, a compute shader is one and a world shader thousands
			var combos = compiling.Select( x => ShaderCompile.CountRecompileCombos( x.Shader.AbsolutePath ) ).ToArray();
			var progress = new Progress( combos.Sum() );
			var timer = FastTimer.StartNew();
			var updated = 0;

			for ( var i = 0; i < compiling.Count; i++ )
			{
				token.ThrowIfCancellationRequested();

				var job = compiling[i];
				var left = compiling.Count - i;
				LoadingScreen.Title = $"Updating Outdated Shaders · {left} left";

				// Named for its source, the .shader they wrote
				LoadingScreen.Subtitle = Path.ChangeExtension( Path.GetFileName( job.Shader.Path ), ".shader" );

				var start = progress.Done;
				var upgraded = await CompileAsync( job, progress, token );

				// Whatever it didn't get to, failing part way
				progress.SkipTo( start + combos[i] );

				if ( upgraded is null )
				{
					failed++;
					continue;
				}

				fileSystem.Repoint( job.Shader.Path, upgraded );
				updated++;
			}

			Log.Info( $"{packageTitle}: updated {updated} of {compiling.Count} outdated {(compiling.Count == 1 ? "shader" : "shaders")} in {timer.Elapsed.Humanize( 2 )}{(failed > 0 ? $", {failed} won't load" : "")}" );
		}
		finally
		{
			LoadingScreen.Title = title;
			LoadingScreen.Subtitle = null;
			LoadingScreen.StepFraction = null;
			gate.Release();
		}
	}

	/// <summary>
	/// Combos compiled out of all of them, onto the loading screen's bar
	/// </summary>
	sealed class Progress( long total )
	{
		long done;

		public long Done
		{
			get { lock ( this ) return done; }
		}

		public void Add()
		{
			lock ( this )
			{
				done++;
				Update();
			}
		}

		public void SkipTo( long combos )
		{
			lock ( this )
			{
				done = Math.Max( done, combos );
				Update();
			}
		}

		void Update() => LoadingScreen.StepFraction = total > 0 ? Math.Clamp( (double)done / total, 0, 1 ) : 0;
	}

	/// <summary>
	/// An outdated shader, where its recompiled copy goes, and whether that's already settled
	/// </summary>
	/// <param name="Shader">The package's outdated shader</param>
	/// <param name="CachePath">Where its recompiled copy goes</param>
	/// <param name="Cached">It's been recompiled already</param>
	/// <param name="FailedBefore">Why it failed on this build before, empty if that isn't known; null if it didn't</param>
	record struct Job( Candidate Shader, string CachePath, bool Cached, string FailedBefore );

	static Job GetJob( Candidate shader, Versions current )
	{
		ulong crc;
		using ( var stream = File.OpenRead( shader.AbsolutePath ) )
			crc = Sandbox.Utility.Crc64.FromStream( stream );

		var cachePath = Path.Combine( CacheFolder, CacheName( shader.Path, crc, current ) );

		if ( File.Exists( cachePath ) )
		{
			if ( GetState( cachePath, current ) == State.Current )
				return new Job( shader, cachePath, true, null );

			File.Delete( cachePath );
		}

		// It failed on this build before, don't spend the time again until the engine changes. The version's on
		// the first line, the compile's output after it.
		var failedPath = cachePath + ".failed";
		if ( File.Exists( failedPath ) )
		{
			var lines = File.ReadAllLines( failedPath );
			if ( lines.Length > 0 && lines[0].Trim() == Application.Version )
				return new Job( shader, cachePath, false, FirstError( lines.Skip( 1 ) ) ?? "" );
		}

		return new Job( shader, cachePath, false, null );
	}

	/// <summary>
	/// Recompile a shader into its cache path. Null if it can't be recompiled.
	/// </summary>
	static async Task<string> CompileAsync( Job job, Progress progress, CancellationToken token )
	{
		var shader = job.Shader;
		var cachePath = job.CachePath;
		var failedPath = cachePath + ".failed";

		var timer = FastTimer.StartNew();

		// Nothing else runs while the game loads, leave a core for the loading screen
		var options = new ShaderCompileOptions
		{
			MaxThreads = Math.Max( 1, ShaderCompile.ComboThreads( default ) - 1 ),
			ComboCompiled = progress.Add,
		};

		ShaderCompile.Results result;

		try
		{
			result = await ShaderCompile.RecompileFromCompiled( shader.AbsolutePath, options, token );
		}
		catch ( Exception e ) when ( e is not OperationCanceledException )
		{
			Log.Warning( e, $"Couldn't recompile outdated shader {shader.Path} ({e.Message})" );
			result = null;
		}

		if ( result is not { Success: true, CompiledShader: not null } )
		{
			var output = result?.Programs.Where( x => !x.Success ).SelectMany( x => x.Output ?? [] ).ToList() ?? [];
			var error = FirstError( output );
			Log.Warning( $"Couldn't recompile outdated shader {shader.Path}, it won't load{(error is null ? "" : $": {error}")}" );

			// All of it, for whoever wants to know why
			Directory.CreateDirectory( Path.GetDirectoryName( failedPath ) );
			await File.WriteAllLinesAsync( failedPath, output.Prepend( Application.Version ), CancellationToken.None );
			return null;
		}

		// Another instance could be writing the same file
		Directory.CreateDirectory( Path.GetDirectoryName( cachePath ) );
		var temporary = $"{cachePath}.{Guid.NewGuid():N}.tmp";
		await File.WriteAllBytesAsync( temporary, result.CompiledShader, CancellationToken.None );
		File.Move( temporary, cachePath, true );

		Log.Info( $"Recompiled outdated shader {shader.Path} in {timer.Elapsed.Humanize( 3 )}" );
		return cachePath;
	}

	static readonly Regex ErrorLine = new( @"\berr(or)?:", RegexOptions.IgnoreCase | RegexOptions.Compiled );

	/// <summary>
	/// The line of a compile's output that says what went wrong - the first error, not the warnings ahead of it -
	/// with the HLSL parser's pointer at the line it choked on. Null if there's no output.
	/// </summary>
	internal static string FirstError( IEnumerable<string> output )
	{
		var lines = output.Select( x => x.Trim() ).Where( x => x.Length > 0 ).ToList();

		var error = lines.FirstOrDefault( ErrorLine.IsMatch ) ?? lines.FirstOrDefault();
		if ( error is null )
			return null;

		var pointer = lines.FirstOrDefault( x => x.StartsWith( "====>" ) );
		return pointer is null ? error : $"{error} at {pointer[5..].Trim()}";
	}

	/// <summary>
	/// Shaders compiled for other versions won't be used again
	/// </summary>
	static void PruneCache( Versions current )
	{
		if ( pruned )
			return;

		pruned = true;

		if ( !Directory.Exists( CacheFolder ) )
			return;

		var suffix = $".{current}.shader_c";

		foreach ( var file in Directory.EnumerateFiles( CacheFolder, "*", SearchOption.AllDirectories ) )
		{
			var keep = file.EndsWith( suffix, StringComparison.OrdinalIgnoreCase ) || file.EndsWith( suffix + ".failed", StringComparison.OrdinalIgnoreCase );
			if ( keep )
				continue;

			try
			{
				File.Delete( file );
			}
			catch ( IOException e )
			{
				Log.Warning( e, $"Couldn't delete {file}" );
			}
		}
	}
}
