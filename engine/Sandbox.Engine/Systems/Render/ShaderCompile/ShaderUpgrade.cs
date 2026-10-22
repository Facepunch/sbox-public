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
			Log.Info( $"{packageTitle}: {outdated.Length} outdated {(outdated.Length == 1 ? "shader" : "shaders")} to update" );

			PruneCache( current );
			LoadingScreen.Title = "Updating Shaders";

			for ( var i = 0; i < outdated.Length; i++ )
			{
				token.ThrowIfCancellationRequested();

				var shader = outdated[i];
				LoadingScreen.Subtitle = $"{Path.GetFileName( shader.Path )} ({i + 1}/{outdated.Length})";

				var upgraded = await GetUpgradedAsync( shader, current, token );
				if ( upgraded is not null )
					fileSystem.Repoint( shader.Path, upgraded );
			}
		}
		finally
		{
			LoadingScreen.Title = title;
			LoadingScreen.Subtitle = null;
			gate.Release();
		}
	}

	/// <summary>
	/// The recompiled copy of a shader, compiling it if it isn't cached. Null if it can't be recompiled.
	/// </summary>
	static async Task<string> GetUpgradedAsync( Candidate shader, Versions current, CancellationToken token )
	{
		var crc = await Task.Run( () =>
		{
			using var stream = File.OpenRead( shader.AbsolutePath );
			return Sandbox.Utility.Crc64.FromStream( stream );
		}, token );

		var cachePath = Path.Combine( CacheFolder, CacheName( shader.Path, crc, current ) );
		var failedPath = cachePath + ".failed";

		if ( File.Exists( cachePath ) )
		{
			if ( GetState( cachePath, current ) == State.Current )
				return cachePath;

			File.Delete( cachePath );
		}

		// It failed on this build before, don't spend the time again until the engine changes
		if ( File.Exists( failedPath ) && File.ReadAllText( failedPath ).Trim() == Application.Version )
		{
			Log.Warning( $"{shader.Path} is outdated and didn't recompile last time, it won't load" );
			return null;
		}

		var timer = FastTimer.StartNew();

		// Nothing else runs while the game loads, leave a core for the loading screen
		var options = new ShaderCompileOptions { MaxThreads = Math.Max( 1, ShaderCompile.ComboThreads( default ) - 1 ) };
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
			var errors = result?.Programs.Where( x => !x.Success ).SelectMany( x => x.Output ?? [] ).FirstOrDefault();
			Log.Warning( $"Couldn't recompile outdated shader {shader.Path}, it won't load{(errors is null ? "" : $": {errors}")}" );

			Directory.CreateDirectory( Path.GetDirectoryName( failedPath ) );
			await File.WriteAllTextAsync( failedPath, Application.Version, CancellationToken.None );
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
