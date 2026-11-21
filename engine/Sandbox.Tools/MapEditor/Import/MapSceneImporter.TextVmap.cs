using System;
using System.Globalization;
using System.IO;
using System.Text;
using N = System.Numerics;

namespace Editor.MapEditor;

static partial class MapSceneImporter
{
	sealed class TextVmap( string text, int version )
	{
		int cursor;
		readonly List<Element> elements = [];
		readonly Dictionary<Guid, Element> ids = [];
		readonly List<(Element Owner, string Key, Element[] Array, int Index, Guid Id)> references = [];

		public Element[] Read()
		{
			while ( Peek() != '\0' ) ReadElement( Word() );
			foreach ( var reference in references )
			{
				ids.TryGetValue( reference.Id, out var target );
				if ( reference.Array is null ) reference.Owner[reference.Key] = target;
				else reference.Array[reference.Index] = target;
			}
			return elements.ToArray();
		}

		Element ReadElement( string type )
		{
			Expect( '{' );
			if ( version < 4 )
			{
				int delimiter = type.IndexOf( ':' );
				if ( delimiter >= 0 ) type = type[..delimiter];
			}
			var element = new Element( type, "" );
			if ( type != "$prefix_element$" ) elements.Add( element );
			while ( Peek() != '}' )
			{
				var key = Word();
				var field = Word();
				if ( Peek() == '{' )
				{
					element[key] = ReadElement( field );
					continue;
				}
				if ( field == "elementid" )
				{
					var id = Token();
					if ( key.Equals( "id", StringComparison.OrdinalIgnoreCase ) ) ids.Add( Guid.Parse( id ), element );
					continue;
				}

				field = field.ToLowerInvariant();
				if ( field.EndsWith( "_array", StringComparison.Ordinal ) )
				{
					Expect( '[' );
					var itemType = field[..^6];
					element[key] = itemType == "element" ? ReadElements() : ReadValue( itemType, array: true );
				}
				else if ( field == "element" )
				{
					var id = Word();
					element[key] = null;
					if ( id.Length > 0 ) references.Add( (element, key, null, 0, Guid.Parse( id )) );
				}
				else if ( key == "name" && field == "string" ) element.Name = Word();
				else element[key] = ReadValue( field, array: false );
			}
			cursor++;
			return element;
		}

		Element[] ReadElements()
		{
			var children = new List<Element>();
			var pending = new List<(int Index, Guid Id)>();
			while ( Peek() != ']' )
			{
				var name = Word();
				if ( name == "element" )
				{
					var id = Word();
					if ( id.Length > 0 ) pending.Add( (children.Count, Guid.Parse( id )) );
					children.Add( null );
				}
				else children.Add( ReadElement( name ) );
				if ( Peek() == ',' ) cursor++;
			}
			cursor++;
			var result = children.ToArray();
			foreach ( var reference in pending ) references.Add( (null, null, result, reference.Index, reference.Id) );
			return result;
		}

		object ReadValue( string type, bool array )
		{
			object Read<T>( Func<ReadOnlySpan<char>, T> parse )
			{
				if ( !array ) return parse( Token() );
				var items = new List<T>();
				while ( Peek() != ']' )
				{
					items.Add( parse( Token() ) );
					if ( Peek() == ',' ) cursor++;
				}
				cursor++;
				return items.ToArray();
			}

			return type switch
			{
				"string" or "elementid" => Read( static s => s.ToString() ),
				"bool" when array => Read( static s => Integer( s ) != 0 ? (byte)1 : (byte)0 ),
				"bool" => Read( static s => Integer( s ) != 0 ),
				"int" => Read( Integer ),
				"float" => Read( static s => float.Parse( s, CultureInfo.InvariantCulture ) ),
				"time" => Read( static s => (int)Math.Round( double.Parse( s, CultureInfo.InvariantCulture ) * 10000 ) ),
				"uint64" => Read( static s => Unsigned( s, hex: true ) ),
				"uint8" => Read( UnsignedByte ),
				"vector2" => Read( static s => { var v = Components( s, 2 ); return new N.Vector2( v.X, v.Y ); } ),
				"vector3" or "qangle" => Read( static s => { var v = Components( s, 3 ); return new N.Vector3( v.X, v.Y, v.Z ); } ),
				"vector4" or "quaternion" => Read( static s => Components( s, 4 ) ),
				"color" => Read( ReadColor ),
				"binary" or "matrix" => Read<object>( static _ => null ),
				_ => throw new InvalidDataException( $"Unknown VMAP attribute type '{type}'." )
			};
		}

		static byte UnsignedByte( ReadOnlySpan<char> text )
		{
			// Hammer scans into a uint16 before checking that the result fits in a byte.
			ushort value = unchecked((ushort)Unsigned( text, hex: false ));
			return checked((byte)value);
		}

		// DMX scans uint64 in hex and uint8 in decimal, stopping at the first non-digit.
		static ulong Unsigned( ReadOnlySpan<char> text, bool hex )
		{
			var digits = IntegerDigits( ref text, hex, out bool negative );
			if ( !ulong.TryParse( digits, hex ? NumberStyles.AllowHexSpecifier : NumberStyles.None, CultureInfo.InvariantCulture, out var value ) )
				return ulong.MaxValue;
			return negative ? unchecked(0UL - value) : value;
		}

		static Color32 ReadColor( ReadOnlySpan<char> text )
		{
			unchecked
			{
				byte r = (byte)ReadInteger( ref text );
				byte g = (byte)ReadInteger( ref text );
				byte b = (byte)ReadInteger( ref text );
				byte a = (byte)ReadInteger( ref text );
				return new Color32( r, g, b, a );
			}
		}

		static int Integer( ReadOnlySpan<char> text ) => ReadInteger( ref text );

		static int ReadInteger( ref ReadOnlySpan<char> text )
		{
			// Both int and bool read a decimal int64, then narrow to int32.
			var digits = IntegerDigits( ref text, false, out bool negative );
			if ( !long.TryParse( digits, NumberStyles.None, CultureInfo.InvariantCulture, out var value ) )
				return unchecked((int)(negative ? long.MinValue : long.MaxValue));
			return unchecked((int)(negative ? -value : value));
		}

		static ReadOnlySpan<char> IntegerDigits( ref ReadOnlySpan<char> text, bool hex, out bool negative )
		{
			text = text.TrimStart();
			negative = text.StartsWith( "-" );
			if ( negative || text.StartsWith( "+" ) ) text = text[1..];
			if ( hex && text.StartsWith( "0x", StringComparison.OrdinalIgnoreCase ) ) text = text[2..];
			int length = 0;
			while ( length < text.Length && (hex ? char.IsAsciiHexDigit( text[length] ) : char.IsAsciiDigit( text[length] )) ) length++;
			if ( length == 0 ) throw new InvalidDataException( "Missing integer in VMAP." );
			var digits = text[..length];
			text = text[length..];
			return digits;
		}

		static N.Vector4 Components( ReadOnlySpan<char> value, int expected )
		{
			Span<Range> ranges = stackalloc Range[4];
			var span = value.Trim();
			if ( span.SplitAny( ranges, " \t\r\n\v\f", StringSplitOptions.RemoveEmptyEntries ) != expected )
				throw new InvalidDataException( $"Expected {expected} vector components in VMAP." );
			var result = N.Vector4.Zero;
			for ( int i = 0; i < expected; i++ ) result[i] = float.Parse( span[ranges[i]], CultureInfo.InvariantCulture );
			return result;
		}

		char Peek()
		{
			while ( cursor < text.Length )
			{
				if ( char.IsWhiteSpace( text[cursor] ) ) { cursor++; continue; }
				if ( text[cursor] != '/' || cursor + 1 == text.Length || text[cursor + 1] != '/' ) return text[cursor];
				int end = text.IndexOf( '\n', cursor );
				cursor = end < 0 ? text.Length : end + 1;
			}
			return '\0';
		}

		void Expect( char ch )
		{
			if ( Peek() != ch ) throw Error();
			cursor++;
		}

		string Word() => Token().ToString();

		ReadOnlySpan<char> Token()
		{
			Expect( '"' );
			int start = cursor;
			StringBuilder escaped = null;
			while ( true )
			{
				int length = text.AsSpan( start ).IndexOfAny( '"', '\\' );
				if ( length < 0 ) throw Error();
				cursor = start + length + 1;
				if ( text[cursor - 1] == '"' )
				{
					if ( escaped is null ) return text.AsSpan( start, length );
					var value = escaped.Append( text, start, length ).ToString().AsSpan();
					// Hammer decodes unknown escapes as NUL, terminating the resulting C string.
					int end = value.IndexOf( '\0' );
					return end < 0 ? value : value[..end];
				}
				escaped ??= new StringBuilder();
				escaped.Append( text, start, length );
				if ( cursor == text.Length ) throw Error();
				char escape = text[cursor++];
				escaped.Append( escape switch
				{
					'a' => '\a',
					'b' => '\b',
					'f' => '\f',
					'n' => '\n',
					'r' => '\r',
					't' => '\t',
					'v' => '\v',
					'\\' or '"' or '\'' or '?' => escape,
					_ => '\0'
				} );
				start = cursor;
			}
		}

		InvalidDataException Error() => new( $"Invalid text VMAP at offset {cursor}." );
	}
}
