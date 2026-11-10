using System;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace Sandbox.Mounting.Source1;

internal readonly record struct BspEntityConnection( string Output, string Target, string Input, string Parameter, float Delay, int FireCount )
{
	internal static IEnumerable<MapEntity> ResolveTargets( MapEntity source, string name, IEnumerable<MapEntity> entities )
	{
		if ( name.Equals( "!self", StringComparison.OrdinalIgnoreCase ) || name.Equals( "!caller", StringComparison.OrdinalIgnoreCase ) )
			return entities.Where( entity => ReferenceEquals( entity, source ) );
		if ( name.StartsWith( '!' ) )
			return [];
		var pattern = name.Contains( '*' ) ? new Regex( $"^{Regex.Escape( name ).Replace( "\\*", ".*" )}$",
			RegexOptions.IgnoreCase | RegexOptions.CultureInvariant ) : null;
		var candidates = entities as IReadOnlyList<MapEntity> ?? entities.ToArray();
		bool Matches( string value ) => pattern is null ? string.Equals( value, name, StringComparison.OrdinalIgnoreCase ) : pattern.IsMatch( value ?? "" );
		var named = candidates.Where( entity => Matches( entity.Get( "targetname" ) ) ).ToArray();
		return named.Length > 0 ? named : candidates.Where( entity => Matches( entity.Get( "classname" ) ) );
	}

	internal static BspEntityConnection Read( string output, string value )
	{
		var parts = value.Split( value.Contains( '\x1b' ) ? '\x1b' : ',' );
		if ( parts.Length is < 2 or > 5 || string.IsNullOrWhiteSpace( parts[0] ) )
			throw new InvalidDataException( $"Invalid Source output '{output}': '{value}'." );
		var delay = 0f;
		if ( parts.Length > 3 && parts[3].Length > 0
			&& (!float.TryParse( parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out delay ) || !float.IsFinite( delay )) )
			throw new InvalidDataException( $"Source output '{output}' has an invalid delay." );
		var count = -1;
		if ( parts.Length > 4 && parts[4].Length > 0 && !int.TryParse( parts[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out count ) )
			throw new InvalidDataException( $"Source output '{output}' has an invalid fire count." );
		return new( output, parts[0], parts[1].Length == 0 ? "Use" : parts[1], parts.Length > 2 ? parts[2] : "",
			Math.Max( 0, delay ), count == 0 ? -1 : count );
	}
}
