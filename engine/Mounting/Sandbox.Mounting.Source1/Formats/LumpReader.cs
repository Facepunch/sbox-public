using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Sandbox.Mounting.Source1;

internal static class LumpReader<TLump> where TLump : struct
{
	[Sandbox.SkipHotload]
	private static readonly int Size = Marshal.SizeOf<TLump>();
	[Sandbox.SkipHotload]
	private static readonly bool CanCopy = !RuntimeHelpers.IsReferenceOrContainsReferences<TLump>()
		&& Unsafe.SizeOf<TLump>() == Size && HasBlittableLayout( typeof( TLump ) );

	private static bool HasBlittableLayout( Type type )
	{
		if ( type == typeof( bool ) || type == typeof( char ) || type == typeof( decimal ) )
			return false;
		if ( type.IsPrimitive || type.IsEnum || type.IsPointer )
			return true;
		foreach ( var field in type.GetFields( BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic ) )
			if ( field.IsDefined( typeof( MarshalAsAttribute ), false ) || !HasBlittableLayout( field.FieldType ) )
				return false;
		return true;
	}

	public static void ValidateRange( Stream stream, long offset, int count )
	{
		if ( count < 0 || count > int.MaxValue / Size || offset < 0
			|| (stream.CanSeek && (offset > stream.Length || (long)count * Size > stream.Length - offset)) )
			throw new InvalidDataException( $"Invalid {typeof( TLump ).Name} range (offset {offset}, count {count})." );
	}

	public static TLump[] ReadLump( byte[] src, int offset, int length )
	{
		ArgumentNullException.ThrowIfNull( src );
		if ( offset < 0 || length < 0 || offset > src.Length - length || length % Size != 0 )
			throw new InvalidDataException( $"Invalid {typeof( TLump ).Name} lump range." );

		var array = new TLump[length / Size];
		if ( length == 0 )
			return array;

		if ( CanCopy )
		{
			MemoryMarshal.Cast<byte, TLump>( src.AsSpan( offset, length ) ).CopyTo( array );
			return array;
		}

		// Inline marshalled arrays are not blittable. Pin once, rather than allocating
		// and copying an unmanaged scratch buffer for every record.
		var handle = GCHandle.Alloc( src, GCHandleType.Pinned );
		try
		{
			var start = IntPtr.Add( handle.AddrOfPinnedObject(), offset );
			for ( var i = 0; i < array.Length; ++i )
				array[i] = Marshal.PtrToStructure<TLump>( IntPtr.Add( start, i * Size ) );
		}
		finally
		{
			handle.Free();
		}

		return array;
	}

	public static TLump ReadSingleFromStream( Stream stream )
	{
		ValidateRange( stream, stream.CanSeek ? stream.Position : 0, 1 );
		Span<byte> bytes = Size <= 1024 ? stackalloc byte[Size] : new byte[Size];
		stream.ReadExactly( bytes );
		if ( CanCopy )
			return MemoryMarshal.Read<TLump>( bytes );

		unsafe
		{
			fixed ( byte* data = bytes )
				return Marshal.PtrToStructure<TLump>( (IntPtr)data );
		}
	}

	public static void ReadLumpFromStream( Stream stream, int count, Action<TLump> handler, bool reseekPerItem = true )
	{
		ReadLumpFromStream( stream, count, ( index, lump ) => handler( lump ), reseekPerItem );
	}

	public static void ReadLumpFromStream( Stream stream, int count, Action<int, TLump> handler, bool reseekPerItem = true )
	{
		ArgumentNullException.ThrowIfNull( handler );
		var start = stream.Position;
		var items = ReadLumpFromStream( stream, count );
		var end = stream.Position;

		// Each invocation owns its records: callbacks may recursively read the same type.
		for ( var i = 0; i < count; ++i )
		{
			if ( reseekPerItem ) stream.Seek( start + (long)i * Size, SeekOrigin.Begin );
			handler( i, items[i] );
		}

		if ( reseekPerItem ) stream.Seek( end, SeekOrigin.Begin );
	}

	public static TLump[] ReadLumpFromStream( Stream stream, int count )
	{
		ValidateRange( stream, stream.CanSeek ? stream.Position : 0, count );
		if ( count == 0 )
			return Array.Empty<TLump>();

		if ( CanCopy )
		{
			var array = GC.AllocateUninitializedArray<TLump>( count );
			stream.ReadExactly( MemoryMarshal.AsBytes( array.AsSpan() ) );
			return array;
		}

		var bytes = new byte[count * Size];
		stream.ReadExactly( bytes );
		return ReadLump( bytes, 0, bytes.Length );
	}

}
