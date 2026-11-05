using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using static Sandbox.Mounting.Source1.BspGeometry;

namespace Sandbox.Mounting.Source1;

internal sealed class MapEntity( Dictionary<string, string> values, IReadOnlyList<KeyValuePair<string, string>> entries = null )
{
	internal IReadOnlyList<KeyValuePair<string, string>> KeyValues { get; } = entries ?? values.ToArray();
	public string Get( string key, string fallback = null ) => values.GetValueOrDefault( key, fallback );
	public Angles Angles
	{
		get
		{
			var angles = Angles.Zero;
			foreach ( var entry in KeyValues )
			{
				if ( entry.Key.Equals( "angles", StringComparison.OrdinalIgnoreCase ) )
				{
					var value = ParseVector( entry.Value, "angles" );
					angles = new Angles( value.x, value.y, value.z );
				}
				else if ( entry.Key.Equals( "angle", StringComparison.OrdinalIgnoreCase ) )
				{
					var yaw = ParseNumber( entry.Value, "angle" );
					angles = yaw >= 0 ? new Angles( angles.pitch, yaw, angles.roll )
						: MathF.Truncate( yaw ) == -1 ? new Angles( -90, 0, 0 ) : new Angles( 90, 0, 0 );
				}
			}
			return angles;
		}
	}

	public int Integer( string key, int fallback = 0 )
	{
		if ( !values.TryGetValue( key, out var text ) )
			return fallback;
		if ( int.TryParse( text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value ) )
			return value;
		throw InvalidValue( key, "an integer" );
	}

	public float Number( string key, float fallback = 0 )
	{
		if ( !values.TryGetValue( key, out var text ) )
			return fallback;
		return ParseNumber( text, key );
	}

	public Vector3 Vector( string key, Vector3 fallback = default )
	{
		if ( !values.TryGetValue( key, out var text ) )
			return fallback;
		return ParseVector( text, key );
	}

	private Vector3 ParseVector( ReadOnlySpan<char> text, string key )
	{
		var remaining = text;
		Span<float> parts = stackalloc float[3];
		for ( var i = 0; i < parts.Length; i++ )
		{
			remaining = remaining.TrimStart();
			var length = 0;
			while ( length < remaining.Length && !char.IsWhiteSpace( remaining[length] ) )
				length++;
			if ( length == 0 )
				throw InvalidValue( key, "three finite numbers" );
			parts[i] = ParseNumber( remaining[..length], key );
			remaining = remaining[length..];
		}

		if ( !remaining.IsWhiteSpace() )
			throw InvalidValue( key, "three finite numbers" );
		return new Vector3( parts[0], parts[1], parts[2] );
	}

	private float ParseNumber( ReadOnlySpan<char> text, string key )
	{
		if ( float.TryParse( text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value ) && float.IsFinite( value ) )
			return value;
		throw InvalidValue( key, "a finite number" );
	}

	private InvalidDataException InvalidValue( string key, string expected ) => new( $"Entity '{Get( "classname" )}' named '{Get( "targetname", "<unnamed>" )}' has invalid '{key}'; expected {expected}." );
}
