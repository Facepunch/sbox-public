using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Editor;

/// <summary>
/// Tracks the files reported by the current compile jobs for stage cleanup and statistics.
/// </summary>
internal sealed class SceneCompileOutputs( string source )
{
	readonly DirectoryInfo root = new FileInfo( SceneResourceCompiler.GetBakePath( source ) ).Directory;
	readonly Dictionary<string, HashSet<string>> groups = new();
	string runtimeSceneToDelete;

	internal IEnumerable<string> Groups => groups.Keys;

	internal void Observe( string group, IEnumerable<string> paths )
	{
		foreach ( var path in paths )
		{
			if ( !Resolve( path ).Exists ) continue;

			if ( !groups.TryGetValue( group, out var files ) )
				groups.Add( group, files = new( StringComparer.OrdinalIgnoreCase ) );

			files.Add( path.Replace( '\\', '/' ) );
		}
	}

	internal IEnumerable<(string Group, FileInfo File)> Files()
	{
		foreach ( var (group, paths) in groups )
		{
			foreach ( var path in paths )
			{
				var file = Resolve( path );
				if ( file.Exists ) yield return (group, file);
			}
		}
	}

	internal ISceneCompileCleanup CleanupFor( string group ) => new Cleanup( this, group );

	// The runtime resource sits beside the scene. Only its Tools job can queue this deletion.
	internal void QueueRuntimeSceneDeletion( Asset asset ) => runtimeSceneToDelete = asset.GetCompiledFile( true );

	sealed class Cleanup( SceneCompileOutputs outputs, string group ) : ISceneCompileCleanup
	{
		/// <inheritdoc/>
		public void DeleteOutput( string relativePath ) => outputs.Observe( group, [relativePath] );
	}

	internal void Clear( IReadOnlySet<string> selected, string assetPath )
	{
		if ( selected is null )
		{
			if ( Directory.Exists( root.FullName ) )
				root.Delete( recursive: true );

			groups.Clear();
		}
		else
		{
			var keep = ReferencedOutputs( source, assetPath );
			foreach ( var (group, paths) in groups )
			{
				if ( !selected.Contains( group ) ) keep.UnionWith( paths );
			}

			foreach ( var group in selected )
			{
				if ( !groups.TryGetValue( group, out var paths ) ) continue;
				foreach ( var path in paths.Except( keep, StringComparer.OrdinalIgnoreCase ) )
				{
					var file = Resolve( path );
					if ( file.Exists ) file.Delete();
				}
			}
		}

		if ( File.Exists( runtimeSceneToDelete ) ) File.Delete( runtimeSceneToDelete );
		runtimeSceneToDelete = null;
	}

	FileInfo Resolve( string path )
	{
		if ( string.IsNullOrWhiteSpace( path ) || Path.IsPathRooted( path ) )
			throw new ArgumentException( "Expected a file path relative to the scene data folder.", nameof( path ) );

		foreach ( var part in path.Replace( '\\', '/' ).Split( '/' ) )
		{
			if ( part.Length == 0 || part.EndsWith( '.' ) || part.EndsWith( ' ' ) || SafePath.IsReservedDeviceName( part ) || part.IndexOfAny( Path.GetInvalidFileNameChars() ) >= 0 )
				throw new ArgumentException( $"Invalid scene output path '{path}'.", nameof( path ) );
		}

		var file = new FileInfo( Path.Combine( root.FullName, path ) );
		if ( file.Exists && (file.Attributes & FileAttributes.ReparsePoint) != 0 )
			throw new IOException( $"Expected a local scene output file at '{file.FullName}'." );

		for ( var directory = file.Directory; directory is not null; directory = directory.Parent )
		{
			if ( directory.Exists && (directory.Attributes & FileAttributes.ReparsePoint) != 0 )
				throw new IOException( $"Expected a local scene output folder at '{directory.FullName}'." );

			if ( directory.FullName == root.FullName ) break;
		}

		return file;
	}

	static HashSet<string> ReferencedOutputs( string source, string assetPath )
	{
		var prefix = Path.ChangeExtension( assetPath, null ).Replace( '\\', '/' ) + "_scene_data/";
		var paths = new HashSet<string>( StringComparer.OrdinalIgnoreCase );
		var bytes = File.ReadAllBytes( source ).AsSpan();
		var preamble = System.Text.Encoding.UTF8.Preamble;
		if ( bytes.StartsWith( preamble ) ) bytes = bytes[preamble.Length..];
		var reader = new Utf8JsonReader( bytes, new JsonReaderOptions { MaxDepth = 512, CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true } );
		while ( reader.Read() )
		{
			if ( reader.TokenType != JsonTokenType.String ) continue;
			var path = reader.GetString().Replace( '\\', '/' );
			if ( !path.StartsWith( prefix, StringComparison.OrdinalIgnoreCase ) ) continue;
			path = path[prefix.Length..];
			paths.Add( path );
			paths.Add( path + "_c" );
		}
		return paths;
	}
}

public sealed partial class SceneCompileSession
{
	SceneCompileOutputs _outputs;

	string[] OutputSummary( Asset asset )
	{
		var files = _outputs.Files().ToList();
		if ( File.Exists( asset.GetCompiledFile( true ) ) )
			files.Add( (SceneCompiler.Group, new FileInfo( asset.GetCompiledFile( true ) )) );

		var lines = files.GroupBy( x => (x.Group, x.File.Extension) ).Select( g =>
			$"{g.Key.Group} — {g.Count():n0} {g.Key.Extension.TrimStart( '.' )}, {g.Sum( x => x.File.Length ).FormatBytes()}" ).ToList();
		var unique = files.DistinctBy( x => x.File.FullName, StringComparer.OrdinalIgnoreCase ).ToArray();
		lines.Add( $"Total — {unique.Length:n0} files, {unique.Sum( x => x.File.Length ).FormatBytes()}" );
		return lines.ToArray();
	}
}
