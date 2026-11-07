using System.IO;

namespace Sandbox;

internal static class SafePath
{
	static readonly string[] DeviceNames = ["CON", "PRN", "AUX", "NUL", "CONIN$", "CONOUT$", "CLOCK$"];

	internal static bool IsReservedDeviceName( ReadOnlySpan<char> segment )
	{
		var dot = segment.IndexOf( '.' );
		var name = (dot >= 0 ? segment[..dot] : segment).TrimEnd( ' ' );

		foreach ( var device in DeviceNames )
		{
			if ( name.Equals( device, StringComparison.OrdinalIgnoreCase ) )
				return true;
		}

		if ( name.Length != 4 ) return false;
		if ( !name.StartsWith( "COM", StringComparison.OrdinalIgnoreCase ) && !name.StartsWith( "LPT", StringComparison.OrdinalIgnoreCase ) ) return false;

		return char.IsAsciiDigit( name[3] ) || name[3] is '¹' or '²' or '³';
	}

	internal static string ToFileName( string name, string fallback, int maxLength = 64 )
	{
		if ( string.IsNullOrWhiteSpace( name ) ) return fallback;

		var sb = new StringBuilder( Math.Min( name.Length, maxLength ) );
		foreach ( var c in name )
		{
			if ( sb.Length >= maxLength ) break;
			sb.Append( char.IsLetterOrDigit( c ) || c is '_' or '-' or '.' ? c : '_' );
		}

		var result = sb.ToString().Trim( '.' );
		if ( result.Length == 0 || IsReservedDeviceName( result ) ) return fallback;

		return result;
	}

	internal static bool IsWithin( string root, string path )
	{
		if ( string.IsNullOrEmpty( root ) || string.IsNullOrEmpty( path ) ) return false;

		var fullRoot = Path.TrimEndingDirectorySeparator( Path.GetFullPath( root ) ) + Path.DirectorySeparatorChar;
		var fullPath = Path.GetFullPath( path );
		var comparison = OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

		return fullPath.StartsWith( fullRoot, comparison );
	}
}
