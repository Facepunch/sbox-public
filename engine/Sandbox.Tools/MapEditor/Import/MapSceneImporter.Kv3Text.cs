using System;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Editor.MapEditor;

static partial class MapSceneImporter
{
	// Reads the text KV3 used by model state choices and post-processing layer sources.
	internal sealed class Kv3Text( string text )
	{
		int offset;

		void SkipHeader()
		{
			if ( Peek() == '<' && text.AsSpan( offset ).StartsWith( "<!-- kv3" ) )
			{
				int end = text.IndexOf( "-->", offset, StringComparison.Ordinal );
				if ( end < 0 ) throw Invalid();
				offset = end + 3;
			}
		}

		public JsonNode Read()
		{
			SkipHeader();
			var result = Value( ValueToken() );
			if ( Peek() != '\0' ) throw Invalid();
			return result;
		}

		// Hammer applies completed model choices even if a later KV3 field is malformed.
		public JsonObject ReadModelSettings( out string error )
		{
			var root = NewTable();
			error = null;
			try
			{
				SkipHeader();
				if ( ValueToken() == '{' ) ReadMembers( root );
			}
			catch ( Exception e ) when ( e is InvalidDataException or JsonException )
			{
				error = e.Message;
			}
			return root;
		}

		char ValueToken()
		{
			var token = Peek();
			// Resource flags describe dependencies. The scene converter consumes the value itself.
			while ( token is 'r' or 'R' && text.AsSpan( offset ).StartsWith( "resource", StringComparison.OrdinalIgnoreCase ) )
			{
				int start = offset;
				offset += "resource".Length;
				if ( Peek() is not (':' or '+') )
				{
					offset = start;
					break;
				}
				offset++;
				token = Peek();
			}

			return token;
		}

		static JsonObject NewTable() => new( new JsonNodeOptions { PropertyNameCaseInsensitive = true } );

		void ReadMembers( JsonObject obj )
		{
			offset++;
			while ( Peek() != '}' )
			{
				var key = Word();
				if ( Peek() != '=' ) throw Invalid();
				offset++;
				var token = ValueToken();
				if ( token == '{' )
				{
					var child = NewTable();
					obj[key] = child;
					ReadMembers( child );
				}
				else obj[key] = Value( token, key == "m_lut" );
				if ( Peek() == ',' ) offset++;
			}
			offset++;
		}

		JsonNode Value( char token, bool lookupSamples = false )
		{
			switch ( token )
			{
				case '{':
					var obj = NewTable();
					ReadMembers( obj );
					return obj;
				case '[':
					if ( lookupSamples ) return LookupSamples();
					offset++;
					var array = new JsonArray();
					while ( Peek() != ']' )
					{
						array.Add( Value( ValueToken() ) );
						if ( Peek() == ',' ) offset++;
					}
					offset++;
					return array;
				default:
					if ( token == '"' ) return JsonValue.Create( Word() );
					var value = BareWord();
					if ( value is "null" ) return null;
					if ( value is "true" or "false" ) return JsonValue.Create( value is "true" );
					if ( long.TryParse( value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer ) )
						return JsonValue.Create( integer );
					if ( ulong.TryParse( value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var unsigned ) )
						return JsonValue.Create( unsigned );
					if ( double.TryParse( value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number ) )
						return JsonValue.Create( number );
					throw Invalid();
			}
		}

		// A 32³ lookup contains 98,304 numbers. Keep them together instead of
		// creating a JSON node and a string per sample.
		JsonValue LookupSamples()
		{
			offset++;
			var values = new List<float>();
			while ( Peek() != ']' )
			{
				if ( !float.TryParse( BareWord(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value ) ) throw Invalid();
				values.Add( value );
				if ( Peek() == ',' ) offset++;
			}
			offset++;
			return JsonValue.Create( values.ToArray() );
		}

		char Peek()
		{
			while ( offset < text.Length )
			{
				if ( char.IsWhiteSpace( text[offset] ) ) { offset++; continue; }
				var rest = text.AsSpan( offset );
				if ( rest.StartsWith( "//" ) )
				{
					int end = rest.IndexOf( '\n' );
					offset += end < 0 ? rest.Length : end + 1;
					continue;
				}
				if ( rest.StartsWith( "/*" ) )
				{
					int end = rest[2..].IndexOf( "*/" );
					if ( end < 0 ) throw Invalid();
					offset += end + 4;
					continue;
				}
				return text[offset];
			}
			return '\0';
		}

		string Word()
		{
			int start = offset;
			if ( offset < text.Length && text[offset] == '"' )
			{
				offset++;
				while ( offset < text.Length )
				{
					char c = text[offset++];
					if ( c == '"' ) return JsonSerializer.Deserialize<string>( text.AsSpan( start, offset - start ) );
					if ( c == '\\' ) offset++;
				}
				throw Invalid();
			}
			return BareWord().ToString();
		}

		ReadOnlySpan<char> BareWord()
		{
			int start = offset;
			while ( offset < text.Length && !char.IsWhiteSpace( text[offset] ) && !"{}[]=,/".Contains( text[offset] ) ) offset++;
			if ( offset == start ) throw Invalid();
			return text.AsSpan( start, offset - start );
		}

		InvalidDataException Invalid() => new( $"Invalid text KV3 at offset {offset}." );
	}
}
