using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using static Sandbox.Mounting.Source1.BspGeometry;

namespace Sandbox.Mounting.Source1;

internal partial class BspFile
{
	public List<MapEntity> Entities { get; private set; } = [];

	internal static List<MapEntity> ReadEntities( byte[] data )
	{
		var text = Encoding.UTF8.GetString( data ).TrimEnd( '\0' );
		var reader = new EntityReader( text );
		var result = new List<MapEntity>();
		while ( reader.TryRead( out var token, out var structural ) )
		{
			if ( !structural || token != "{" )
				throw new InvalidDataException( "BSP entity must start with '{'." );
			var values = new Dictionary<string, string>( StringComparer.OrdinalIgnoreCase );
			var entries = new List<KeyValuePair<string, string>>();
			while ( true )
			{
				if ( !reader.TryRead( out var key, out structural ) )
					throw new InvalidDataException( "Unterminated BSP entity." );
				if ( structural && key == "}" )
					break;
				if ( structural || !reader.TryRead( out var value, out structural ) || structural )
					throw new InvalidDataException( "BSP entity must contain key/value pairs." );
				values[key] = value;
				entries.Add( new( key, value ) );
			}

			result.Add( new MapEntity( values, entries ) );
		}

		return result;
	}
}

internal ref struct EntityReader( string text )
{
	private int position;
	public bool TryRead( out string token, out bool structural )
	{
		token = null;
		structural = false;
		while ( position < text.Length )
		{
			if ( char.IsWhiteSpace( text[position] ) )
			{
				position++;
				continue;
			}

			if ( text[position] == '/' && position + 1 < text.Length && text[position + 1] == '/' )
			{
				while ( position < text.Length && text[position] != '\n' )
				{
					if ( text[position++] == '\0' )
						throw new InvalidDataException( "Unexpected null in a BSP entity comment." );
				}

				continue;
			}

			break;
		}

		if ( position == text.Length )
			return false;
		var first = text[position++];
		if ( first == '\0' )
			throw new InvalidDataException( "Unexpected null in a BSP entity token." );
		if ( first is '{' or '}' )
		{
			token = first.ToString();
			structural = true;
			return true;
		}

		if ( first != '"' )
		{
			var start = position - 1;
			while ( position < text.Length && !char.IsWhiteSpace( text[position] ) && text[position] is not '{' and not '}' )
			{
				if ( text[position++] == '\0' )
					throw new InvalidDataException( "Unexpected null in a BSP entity token." );
			}

			token = text[start..position];
			return true;
		}

		var segmentStart = position;
		while ( position < text.Length )
		{
			var c = text[position++];
			if ( c == '"' )
			{
				token = text[segmentStart..(position - 1)];
				return true;
			}

			if ( c == '\0' )
				throw new InvalidDataException( "Unexpected null in a BSP entity value." );
		}

		throw new InvalidDataException( "Unterminated quoted BSP entity value." );
	}
}
