using System.IO;

namespace Sandbox;

/// <summary>
/// A game's own shader sources (<c>shaders/rock.slang</c> and what it imports, with the offline compile the editor made
/// beside it, <c>rock.slang_c</c>), found on disk for Borealis.Gpu (<see cref="Borealis.Source2.ContentFiles.ResolveSource"/>).
/// A local project's are where they are. A package's are in the download cache, each named by its hash, so the folder
/// they're in is laid out once by name under <c>.source2/gamesources</c>: there an import finds its file, and the compiler
/// the offline compile beside its source.
/// </summary>
internal sealed partial class BorealisGpuRenderBackend
{
	static readonly string[] SourceExtensions = [".slang", ".slangh", ".slang_c", ".hlsl"];
	static readonly Dictionary<string, string> laidOut = new( StringComparer.OrdinalIgnoreCase );

	internal static string MountedSource( string path )
	{
		foreach ( var context in (Sandbox.Engine.GlobalContext[])[Sandbox.Engine.GlobalContext.Game, Sandbox.Engine.GlobalContext.Menu] )
		{
			if ( context?.FileMount is not { } mount ) continue;
			try
			{
				if ( !mount.FileExists( path ) || mount.GetFullPath( path ) is not { } full ) continue;
				return string.Equals( Path.GetFileName( full ), Path.GetFileName( path ), StringComparison.OrdinalIgnoreCase ) ? full : LayOut( mount, path );
			}
			catch ( IOException )
			{
				// A package mounting or unmounting while a worker looks: not found this time
			}
		}

		return null;
	}

	/// <summary>
	/// <paramref name="path"/>'s folder's shader sources in <paramref name="mount"/>, subfolders and all, copied by name into
	/// a folder named for where they are on disk (a new version of any is a new folder); <paramref name="path"/>'s copy.
	/// </summary>
	static string LayOut( BaseFileSystem mount, string path )
	{
		var folder = Path.GetDirectoryName( path.Replace( '\\', '/' ) )?.Replace( '\\', '/' ) ?? "";
		var files = mount.FindFile( folder, "*", recursive: true )
			.Where( f => SourceExtensions.Contains( Path.GetExtension( f ), StringComparer.OrdinalIgnoreCase ) )
			.Select( f => (Name: f.Replace( '\\', '/' ), Full: mount.GetFullPath( $"{folder}/{f}" )) )
			.Where( f => f.Full is not null )
			.OrderBy( f => f.Name, StringComparer.OrdinalIgnoreCase )
			.ToList();

		var identity = string.Join( "\n", files.Select( f => $"{f.Name}|{f.Full}" ) );
		lock ( laidOut )
		{
			if ( laidOut.TryGetValue( identity, out var known ) ) return Path.Combine( known, Path.GetFileName( path ) );
		}

		var hash = Convert.ToHexString( System.Security.Cryptography.SHA256.HashData( System.Text.Encoding.UTF8.GetBytes( identity ) ) )[..16].ToLowerInvariant();
		var target = Path.Combine( EngineFileSystem.Root.GetFullPath( "/.source2/gamesources" ), hash, folder );
		foreach ( var (name, full) in files )
		{
			var copy = Path.Combine( target, name );
			if ( File.Exists( copy ) ) continue;
			Directory.CreateDirectory( Path.GetDirectoryName( copy ) );
			File.Copy( full, copy + ".tmp", overwrite: true );
			File.Move( copy + ".tmp", copy, overwrite: true );
		}

		lock ( laidOut ) laidOut[identity] = target;
		return Path.Combine( target, Path.GetFileName( path ) );
	}
}
