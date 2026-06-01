using System.Globalization;
using System.Numerics;

namespace Sandbox.Services;

/// <summary>
/// Vectors in shared metadata DTOs are stored as plain <c>"x,y,z"</c> strings so they serialize
/// the same everywhere without any custom JSON converter. Use these helpers to convert to and
/// from <see cref="Vector3"/> on both the website and the game engine.
/// </summary>
public static class MetaVector
{
	/// <summary>Format a vector as the canonical <c>"x,y,z"</c> metadata string.</summary>
	public static string ToString( Vector3 v )
	{
		return string.Create( CultureInfo.InvariantCulture, $"{v.X:G9},{v.Y:G9},{v.Z:G9}" );
	}

	/// <summary>Parse a <c>"x,y,z"</c> metadata string back into a vector (zero if null/empty/malformed).</summary>
	public static Vector3 Parse( string str )
	{
		if ( string.IsNullOrWhiteSpace( str ) )
			return default;

		var parts = str.Trim( '[', ']', ' ', '"' ).Split( ',' );

		Vector3 v = default;
		if ( parts.Length > 0 ) float.TryParse( parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out v.X );
		if ( parts.Length > 1 ) float.TryParse( parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out v.Y );
		if ( parts.Length > 2 ) float.TryParse( parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out v.Z );
		return v;
	}
}
