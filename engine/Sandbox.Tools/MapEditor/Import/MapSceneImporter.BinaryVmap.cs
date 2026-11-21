using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using N = System.Numerics;

namespace Editor.MapEditor;

static partial class MapSceneImporter
{
	internal sealed class Element( string type, string name ) : Dictionary<string, object>
	{
		public string ClassName { get; set; } = type;
		public string Name { get; set; } = name;
		public Element[] SceneChildren
		{
			get
			{
				var children = Get<Element[]>( "children", [] );
				// Empty references and Source 2 navigation compiler settings have no Hammer scene node.
				static bool IsSceneNode( Element child ) => child is { ClassName: not "CMapNavData" };
				return Array.Exists( children, child => !IsSceneNode( child ) ) ? Array.FindAll( children, IsSceneNode ) : children;
			}
		}
		public T Get<T>( string key ) => (T)this[key];
		public T Get<T>( string key, T fallback ) => TryGetValue( key, out var value ) ? (T)value : fallback;
		public T[] GetArray<T>( string key ) => Get<T[]>( key );
		public ReadOnlySpan<T> Numbers<T>( string key, bool optional = false ) where T : unmanaged
		{
			var value = optional ? this.GetValueOrDefault( key ) : this[key];
			if ( value is null && optional ) return default;
			return value is T[] values ? values : ((NumericArray<T>)value).Span;
		}
	}

	readonly record struct NumericArray<T>( ReadOnlyMemory<byte> Bytes ) where T : unmanaged
	{
		public ReadOnlySpan<T> Span => MemoryMarshal.Cast<byte, T>( Bytes.Span );
	}

	// DMX element references index the directory,
	// so allocate the directory before reading attributes (including forward references).
	internal sealed class BinaryVmap( ReadOnlyMemory<byte> bytes, int version )
	{
		// Attribute dictionaries store objects. These values recur on most map nodes.
		static readonly object BoxedTrue = true, BoxedFalse = false, BoxedZero = 0, BoxedOne = 1;

		ReadOnlyMemory<byte> remaining = bytes;
		string[] symbols;
		Element[] elements;

		internal static Element[] Read( Stream stream, string filename )
		{
			Span<byte> header = stackalloc byte[128];
			int length = 0;
			while ( length < header.Length )
			{
				int value = stream.ReadByte();
				if ( value < 0 ) throw new EndOfStreamException( $"Truncated VMAP header in '{filename}'." );
				header[length++] = (byte)value;
				if ( value == '>' ) break;
			}
			var words = Encoding.ASCII.GetString( header[..length] ).Split( ' ', StringSplitOptions.RemoveEmptyEntries );
			if ( words is not ["<!--", "dmx", "encoding", var encoding, var versionText, "format", "vmap", var schemaText, "-->"] ||
				!int.TryParse( versionText, out var version ) || !int.TryParse( schemaText, out var schema ) )
				throw new InvalidDataException( $"Invalid VMAP header in '{filename}'." );
			if ( schema is < 1 or > 30 ) throw new NotSupportedException( $"Unsupported VMAP schema {schema} in '{filename}'." );
			if ( encoding is ("keyvalues2" or "keyvalues2_flat" or "keyvalues2_noids") && version is >= 1 and <= 4 )
			{
				using var text = new StreamReader( stream, leaveOpen: true );
				return Upgrade( new TextVmap( text.ReadToEnd(), version ).Read(), schema );
			}
			if ( encoding != "binary" || version is < 1 or > 9 )
				throw new NotSupportedException( $"Unsupported VMAP encoding {encoding} {version} in '{filename}'." );

			if ( !stream.CanSeek )
			{
				using var buffer = new MemoryStream();
				stream.CopyTo( buffer );
				return Upgrade( new BinaryVmap( buffer.GetBuffer().AsMemory( 0, checked((int)buffer.Length) ), version ).ReadDirectory(), schema );
			}
			var payload = GC.AllocateUninitializedArray<byte>( checked((int)(stream.Length - stream.Position)) );
			stream.ReadExactly( payload );
			return Upgrade( new BinaryVmap( payload, version ).ReadDirectory(), schema );
		}

		Element[] ReadDirectory()
		{
			Text(); // newline and NUL following the header
			if ( version >= 7 )
			{
				// Some early files omitted the prefix count entirely. Hammer treats counts
				// above one as the start of the string table and leaves them unread.
				int prefix = MemoryMarshal.Read<int>( remaining.Span );
				if ( prefix is 0 or 1 ) Take( 4 );
				if ( prefix == 1 ) ReadAttributes( null );
			}
			if ( version >= 2 )
			{
				int count = version >= 4 ? Count( 1 ) : Short();
				symbols = new string[count];
				for ( int i = 0; i < symbols.Length; i++ ) symbols[i] = Text();
			}
			elements = new Element[Count( 18 )];
			for ( int i = 0; i < elements.Length; i++ )
			{
				var type = Symbol();
				if ( version is 6 or 7 ) Symbol(); // obsolete linked schema type
				elements[i] = new Element( type, version >= 4 ? Symbol() : Text() );
				Take( 16 ); // DMX GUID
			}
			foreach ( var element in elements ) ReadAttributes( element );
			return elements;
		}

		void ReadAttributes( Element element )
		{
			int count = Count( 2 );
			element?.EnsureCapacity( count );
			for ( int i = 0; i < count; i++ )
			{
				string key = element is null ? Text() : Symbol();
				int type = Take( 1 )[0];
				if ( version < 9 && type > 14 )
				{
					if ( type > 28 ) throw new InvalidDataException( $"Unknown legacy VMAP attribute type {type} on '{key}'." );
					type = type - 14 + 32;
				}
				bool array = (type & 32) != 0;
				int length = array ? Count( 1 ) : 1;
				object value = (type & ~32) switch
				{
					1 => References( length, array ),
					7 when version < 3 => Skip( checked(16 * length) ), // obsolete object IDs
					2 or 7 when !array => Integer() switch { 0 => BoxedZero, 1 => BoxedOne, var n => n },
					2 or 7 => Values<int>( length, array ),
					3 => Values<float>( length, array ),
					4 when !array => Take( 1 )[0] != 0 ? BoxedTrue : BoxedFalse,
					4 => Take( length ).ToArray(),
					5 => array ? Strings( length ) : element is null || version < 4 ? Text() : Symbol(),
					6 => SkipBlobs( length ),
					8 => Values<Color32>( length, array ),
					9 => Values<N.Vector2>( length, array ),
					10 or 12 => Values<N.Vector3>( length, array ),
					11 or 13 => Values<N.Vector4>( length, array ),
					14 => Skip( checked(length * 64) ),
					15 => Values<ulong>( length, array ),
					16 => Values<byte>( length, array ),
					_ => throw new InvalidDataException( $"Unknown VMAP attribute type {type} on '{key}'." )
				};
				if ( element is not null ) element[key] = value;
			}
		}

		object References( int length, bool array )
		{
			if ( !array ) return Reference();
			if ( length == 0 ) return Array.Empty<Element>();
			var refs = new Element[length];
			for ( int i = 0; i < length; i++ ) refs[i] = Reference();
			return refs;
		}

		Element Reference()
		{
			int index = Integer();
			if ( index == -2 ) Text(); // external DMX GUID
			return index < 0 ? null : elements[index];
		}

		string[] Strings( int length )
		{
			if ( length == 0 ) return [];
			var result = new string[length];
			for ( int i = 0; i < length; i++ ) result[i] = Text();
			return result;
		}

		object Values<T>( int length, bool array ) where T : unmanaged
		{
			if ( length == 0 ) return Array.Empty<T>();
			int size = checked(length * System.Runtime.CompilerServices.Unsafe.SizeOf<T>());
			var bytes = remaining;
			var span = Take( size );
			return array ? new NumericArray<T>( bytes[..size] ) : MemoryMarshal.Read<T>( span );
		}

		string Text()
		{
			int end = remaining.Span.IndexOf( (byte)0 );
			if ( end < 0 ) throw new EndOfStreamException();
			return Encoding.UTF8.GetString( Take( end + 1 )[..end] );
		}

		string Symbol() => symbols is null ? Text() : symbols[version >= 5 ? Integer() : Short()];
		short Short() => MemoryMarshal.Read<short>( Take( 2 ) );
		int Integer() => MemoryMarshal.Read<int>( Take( 4 ) );
		int Count( int minimumSize )
		{
			int count = Integer();
			if ( (uint)count > remaining.Length / minimumSize ) throw new InvalidDataException( "Invalid VMAP array length." );
			return count;
		}

		object SkipBlobs( int count )
		{
			for ( int i = 0; i < count; i++ ) Take( Count( 1 ) );
			return null;
		}

		object Skip( int size )
		{
			Take( size );
			return null;
		}

		ReadOnlySpan<byte> Take( int size )
		{
			if ( (uint)size > remaining.Length ) throw new EndOfStreamException();
			var slice = remaining[..size];
			remaining = remaining[size..];
			return slice.Span;
		}
	}
}
