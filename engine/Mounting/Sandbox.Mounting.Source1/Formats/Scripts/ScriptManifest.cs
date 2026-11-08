using System;
using System.Collections.Generic;
using System.IO;

namespace Sandbox.Mounting.Source1;

internal static class ScriptManifest
{
	internal static IReadOnlyList<VmtNode> Parse( string text, string path, Action<string> report ) =>
		VmtFile.ParseKeyValues( text, path, diagnostic => report( $"{diagnostic.Path}:{diagnostic.Line}: {diagnostic.Message}" ) );

	internal static IEnumerable<string> Read( string text, string path, string rootName, params string[] keys )
	{
		var roots = VmtFile.ParseKeyValues( text, path,
			diagnostic => throw new NotSupportedException( $"{path}: {diagnostic.Message}" ) );
		if ( roots.Count != 1 || !roots[0].IsBlock || !roots[0].Name.Equals( rootName, StringComparison.OrdinalIgnoreCase ) )
			throw new InvalidDataException( $"{path}: expected one '{rootName}' block." );

		var seen = new HashSet<string>( StringComparer.OrdinalIgnoreCase );
		foreach ( var entry in roots[0].Children )
		{
			if ( entry.IsBlock || !Array.Exists( keys, key => key.Equals( entry.Name, StringComparison.OrdinalIgnoreCase ) ) )
				throw new InvalidDataException( $"{path}: unsupported manifest entry '{entry.Name}'." );
			var filename = ContentPath( entry.Value );
			if ( seen.Add( filename ) )
				yield return filename;
		}
	}

	internal static string ContentPath( string path )
	{
		path = path.Replace( '\\', '/' ).Trim();
		if ( string.IsNullOrWhiteSpace( path ) || path.StartsWith( '/' ) || path.Contains( ':' )
			|| Array.Exists( path.Split( '/' ), part => part is "" or "." or ".." ) )
			throw new InvalidDataException( $"Invalid Source content path '{path}'." );
		return path.ToLowerInvariant();
	}
}
