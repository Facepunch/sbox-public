using System.Runtime.InteropServices;
using System.Text;

namespace Steamworks
{
	internal unsafe class Utf8StringToNative : ICustomMarshaler
	{
		public IntPtr MarshalManagedToNative( object managedObj )
		{
			if ( managedObj == null )
				return IntPtr.Zero;

			if ( managedObj is string str )
			{
				fixed ( char* strPtr = str )
				{
					int len = Encoding.UTF8.GetByteCount( str );
					var mem = Marshal.AllocHGlobal( len + 1 );

					var wlen = System.Text.Encoding.UTF8.GetBytes( strPtr, str.Length, (byte*)mem, len + 1 );

					((byte*)mem)[wlen] = 0;

					return mem;
				}
			}

			return IntPtr.Zero;
		}

		public object MarshalNativeToManaged( IntPtr pNativeData ) => throw new System.NotImplementedException();
		public void CleanUpNativeData( IntPtr pNativeData ) => Marshal.FreeHGlobal( pNativeData );
		public void CleanUpManagedData( object managedObj ) => throw new System.NotImplementedException();
		public int GetNativeDataSize() => -1;

		public static ICustomMarshaler GetInstance( string cookie ) => new Utf8StringToNative();
	}

	internal struct Utf8StringPointer
	{
#pragma warning disable 649
		internal IntPtr ptr;
#pragma warning restore 649

		public unsafe static implicit operator string( Utf8StringPointer p )
		{
			if ( p.ptr == IntPtr.Zero )
				return null;

			// Uses the runtime's vectorized strlen to find the null terminator
			var span = MemoryMarshal.CreateReadOnlySpanFromNullTerminated( (byte*)p.ptr );
			return Encoding.UTF8.GetString( span );
		}
	}
}
