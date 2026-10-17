using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;

namespace Steamworks;

/// <summary>
/// Steam packs its callback structs to 4 bytes on Linux and macOS (<c>VALVE_CALLBACK_PACK_SMALL</c> in the Steamworks
/// headers) and to 8 bytes on Windows. Our generated callback structs are declared with the Windows packing
/// (<see cref="Platform.StructPlatformPackSize"/>), so on other platforms any of them with a 64-bit field after a
/// 32-bit one, like <c>LobbyCreated_t</c>, has its fields at the wrong offsets and the wrong size. This reads a
/// struct from the platform's native layout into the managed one, field by field, and tells the size Steam expects.
/// On Windows, and for every struct whose layout is the same either way, it is a plain <see cref="Marshal.PtrToStructure(IntPtr, Type)"/>.
/// </summary>
internal static class NativeLayout
{
	/// <summary>Steam's callback structs are packed to 4 bytes here, not 8.</summary>
	internal static readonly bool SmallPack = !OperatingSystem.IsWindows();

	sealed class Map
	{
		public int NativeSize;
		public int ManagedSize;
		public bool Same;
		/// <summary>Byte ranges: from the native struct, into the managed one.</summary>
		public List<(int From, int To, int Size)> Copies = new();
	}

	static readonly ConcurrentDictionary<Type, Map> Maps = new();

	/// <summary>How big Steam's own struct is on this platform (what GetAPICallResult expects).</summary>
	internal static int NativeSize( Type t ) => Get( t ).NativeSize;

	/// <summary>Read a struct Steam wrote at <paramref name="ptr"/>.</summary>
	internal static object Read( IntPtr ptr, Type t )
	{
		if ( ptr == IntPtr.Zero )
			return default;

		var map = Get( t );
		if ( map.Same )
			return Marshal.PtrToStructure( ptr, t );

		var native = new byte[map.NativeSize];
		Marshal.Copy( ptr, native, 0, native.Length );

		var managed = new byte[map.ManagedSize];
		foreach ( var (from, to, size) in map.Copies )
			Buffer.BlockCopy( native, from, managed, to, size );

		var buffer = Marshal.AllocHGlobal( managed.Length );
		try
		{
			Marshal.Copy( managed, 0, buffer, managed.Length );
			return Marshal.PtrToStructure( buffer, t );
		}
		finally
		{
			Marshal.FreeHGlobal( buffer );
		}
	}

	static Map Get( Type t ) => Maps.GetOrAdd( t, Build );

	static Map Build( Type t )
	{
		var map = new Map { ManagedSize = Marshal.SizeOf( t ) };

		if ( !SmallPack )
		{
			map.NativeSize = map.ManagedSize;
			map.Same = true;
			return map;
		}

		Lay( t, 0, 0, map.Copies, out map.NativeSize, out _ );

		map.Same = map.NativeSize == map.ManagedSize && map.Copies.All( x => x.From == x.To );
		return map;
	}

	/// <summary>
	/// Lay out <paramref name="t"/> as Steam does on this platform, starting at <paramref name="nativeBase"/> in the
	/// native struct and <paramref name="managedBase"/> in the managed one; adds a byte range per field (or per field
	/// of a nested struct).
	/// </summary>
	static void Lay( Type t, int nativeBase, int managedBase, List<(int, int, int)> copies, out int size, out int align )
	{
		var layout = t.StructLayoutAttribute;
		int pack = layout?.Pack ?? 0;

		// Only the structs declared with the Windows callback packing are packed differently by Steam here.
		int cap = pack == Platform.StructPlatformPackSize ? Platform.StructPackSize : (pack > 0 ? pack : 8);

		var fields = t.GetFields( BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic )
			.OrderBy( f => Marshal.OffsetOf( t, f.Name ).ToInt64() )
			.ToArray();

		int offset = 0;
		align = 1;

		foreach ( var f in fields )
		{
			int managedOffset = (int)Marshal.OffsetOf( t, f.Name );
			FieldSize( f, out int fieldSize, out int fieldAlign, out bool nested );

			int a = Math.Min( fieldAlign, cap );
			offset = (offset + a - 1) / a * a;
			align = Math.Max( align, a );

			if ( nested )
			{
				Lay( f.FieldType, nativeBase + offset, managedBase + managedOffset, copies, out int nestedSize, out _ );
				fieldSize = nestedSize;
			}
			else
			{
				copies.Add( (nativeBase + offset, managedBase + managedOffset, fieldSize) );
			}

			offset += fieldSize;
		}

		size = Math.Max( 1, (offset + align - 1) / align * align );
		if ( fields.Length == 0 )
			size = 1;
	}

	static void FieldSize( FieldInfo f, out int size, out int align, out bool nested )
	{
		nested = false;
		var type = f.FieldType;
		var marshalAs = f.GetCustomAttribute<MarshalAsAttribute>();

		if ( marshalAs is not null )
		{
			switch ( marshalAs.Value )
			{
				case UnmanagedType.ByValArray:
					{
						var element = type.GetElementType() ?? typeof( byte );
						int elementSize = ElementSize( element );
						size = elementSize * marshalAs.SizeConst;
						align = AlignOf( element );
						return;
					}
				case UnmanagedType.ByValTStr:
					size = marshalAs.SizeConst;
					align = 1;
					return;
				case UnmanagedType.I1:
				case UnmanagedType.U1:
					size = 1;
					align = 1;
					return;
			}
		}

		if ( type.IsEnum )
			type = Enum.GetUnderlyingType( type );

		if ( type == typeof( bool ) )
		{
			size = 4; // (unmarshalled bool is a 4 byte BOOL)
			align = 4;
			return;
		}

		if ( type.IsPrimitive || type == typeof( IntPtr ) || type == typeof( UIntPtr ) )
		{
			size = Marshal.SizeOf( type );
			align = size;
			return;
		}

		if ( type == typeof( string ) || type.IsClass )
		{
			size = IntPtr.Size; // (a char* marshalled to a string)
			align = IntPtr.Size;
			return;
		}

		// a nested struct (a SteamId, a fixed buffer, ...)
		size = Marshal.SizeOf( type );
		align = AlignOf( type );
		nested = type.IsValueType && !type.IsPrimitive && type.GetFields( BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic ).Length > 0
			&& !type.IsDefined( typeof( System.Runtime.CompilerServices.UnsafeValueTypeAttribute ), false );
	}

	static int ElementSize( Type element )
	{
		if ( element.IsEnum ) element = Enum.GetUnderlyingType( element );
		return element == typeof( bool ) ? 1 : Marshal.SizeOf( element );
	}

	static int AlignOf( Type t )
	{
		if ( t.IsEnum ) t = Enum.GetUnderlyingType( t );
		if ( t == typeof( bool ) ) return 1;
		if ( t.IsPrimitive || t == typeof( IntPtr ) || t == typeof( UIntPtr ) ) return Marshal.SizeOf( t );
		if ( t == typeof( string ) || t.IsClass ) return IntPtr.Size;

		int pack = t.StructLayoutAttribute?.Pack ?? 0;
		int cap = pack == Platform.StructPlatformPackSize && SmallPack ? Platform.StructPackSize : (pack > 0 ? pack : 8);

		int align = 1;
		foreach ( var f in t.GetFields( BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic ) )
		{
			var ft = f.FieldType;
			var marshalAs = f.GetCustomAttribute<MarshalAsAttribute>();
			if ( marshalAs?.Value is UnmanagedType.I1 or UnmanagedType.U1 or UnmanagedType.ByValTStr ) continue;
			if ( marshalAs?.Value == UnmanagedType.ByValArray ) ft = ft.GetElementType() ?? typeof( byte );
			align = Math.Max( align, AlignOf( ft ) );
		}

		return Math.Min( align, cap );
	}
}
