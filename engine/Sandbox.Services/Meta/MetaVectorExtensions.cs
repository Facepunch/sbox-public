using System.Globalization;
using System.Numerics;

namespace Sandbox.Services;

/// <summary>
/// Vectors in shared metadata DTOs are stored as plain <c>"x,y,z"</c> strings so they serialize
/// the same everywhere without any custom JSON converter. These extensions convert to and from
/// <see cref="Vector3"/> on both the website and the game engine.
/// </summary>
public static class MetaVectorExtensions
{
	extension( Vector3 v )
	{
		/// <summary>Format this vector as the canonical <c>"x,y,z"</c> metadata string.</summary>
		public string ToMetaString()
			=> string.Create( CultureInfo.InvariantCulture, $"{v.X},{v.Y},{v.Z}" );
	}

	extension( string str )
	{
		/// <summary>Parse a <c>"x,y,z"</c> metadata string into a vector (zero if null/empty/malformed).</summary>
		public Vector3 ToMetaVector()
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
}
