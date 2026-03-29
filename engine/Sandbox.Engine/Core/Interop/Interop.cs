using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace Sandbox;

internal unsafe static partial class Interop
{
	private static Logger log = Logging.GetLogger();

	[SkipHotload] static List<PassBackString> FrameAllocatedStrings = new();

	public static int Free()
	{
		int i = 0;

		foreach ( var entry in FrameAllocatedStrings )
		{
			entry.Free();
			i++;
		}

		FrameAllocatedStrings.Clear();

		return i;
	}

	const int maxNativeString = 1024 * 1024 * 64; // a 64mb string sounds sensible!

	/// <summary>
	/// Convert a native utf pointer to a string
	/// </summary>
	public static string GetString( IntPtr pointer )
	{
		if ( pointer == IntPtr.Zero )
			return null;

		int length = GetUtf8Length( (byte*)pointer, maxNativeString );

		if ( length < 0 )
		{
			Log.Warning( "Really long, or really invalid string detected" );
			return null;
		}

		return GetString( pointer, length );
	}

	/// <summary>
	/// Convert a native utf pointer to a string
	/// </summary>
	public static string GetString( IntPtr pointer, int byteLen )
	{
		if ( pointer == IntPtr.Zero || byteLen < 0 )
			return null;

		if ( byteLen == 0 )
			return string.Empty;

		return Encoding.UTF8.GetString( (byte*)pointer, byteLen );
	}

	/// <summary>
	/// Get the length of a null-terminated UTF-8 string using AVX2 (fallback to scalar if unavailable)
	/// </summary>
	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	private static int GetUtf8Length( byte* ptr, int maxLen )
	{
		byte* start = ptr;
		int length = 0;

		while ( length < maxLen && ptr[length] != 0 )
			length++;

		return length >= maxLen ? -1 : length;
	}

	/// <summary>
	/// Convert a native utf pointer to a string
	/// </summary>
	public static string GetWString( IntPtr pointer )
	{
		if ( pointer == IntPtr.Zero )
			return null;

		return Marshal.PtrToStringUni( pointer );
	}

	/// <summary>
	/// Convert a native utf pointer to a string
	/// </summary>
	public static string GetWString( IntPtr pointer, int byteLen )
	{
		if ( pointer == IntPtr.Zero || byteLen <= 0 )
			return null;

		return Marshal.PtrToStringUni( pointer, byteLen );
	}

	public unsafe ref struct InteropString
	{
		public IntPtr Pointer;

		public InteropString( string str )
		{
			if ( str is null )
				return;

			uint nb = (uint)Encoding.UTF8.GetByteCount( str );
			byte* mem = (byte*)NativeMemory.Alloc( nb + 1 );

			fixed ( char* src = str )
			{
				Encoding.UTF8.GetBytes( src, str.Length, mem, (int)nb );
			}

			mem[nb] = 0;
			Pointer = (IntPtr)mem;
		}

		public void Free()
		{
			NativeMemory.Free( (void*)Pointer );
			Pointer = default;
		}
	}

	/// <summary>
	/// Called by the binding system to log an exception when calling a binding
	/// </summary>
	public static void BindingException( string ClassName, string MethodName, Exception e )
	{
		try
		{
			log.Error( e, e.Message );
		}
		catch ( Exception e2 )
		{
			System.Diagnostics.Debug.WriteLine( "Exception thrown when logging exception: {0}", e2 );
			System.Diagnostics.Debug.WriteLine( "Original exception: {0}", e );
		}
	}

	internal static void NativeAssemblyLoadFailed( string libraryName )
	{
		string errorMessage = $"Failed to load native library '{libraryName}'. Error Code: {Marshal.GetLastWin32Error()}/{Marshal.GetLastSystemError()}";

		throw new NativeAssemblyLoadException( errorMessage, Marshal.GetLastWin32Error() );
	}

	/// <summary>
	/// used to pass a string back to native
	/// </summary>
	public unsafe struct PassBackString
	{
		public IntPtr Pointer;

		public PassBackString( string str )
		{
			if ( str is null )
				return;

			uint nb = (uint)Encoding.UTF8.GetByteCount( str );
			byte* mem = (byte*)NativeMemory.Alloc( nb + 1 );

			fixed ( char* src = str )
			{
				Encoding.UTF8.GetBytes( src, str.Length, mem, (int)nb );
			}

			mem[nb] = 0;
			Pointer = (IntPtr)mem;
		}

		public void Free()
		{
			NativeMemory.Free( (void*)Pointer );
			Pointer = default;
		}
	}

	/// <summary>
	/// This is called when native calls a managed function and it returns a string. In this case
	/// we can't free the string immediately, so we store it in a list and free it at the end of the frame.
	/// This has potential to crash, if we free the string before the thread uses it but this would be super 
	/// rare and the other option is to never return strings like this.
	/// </summary>
	internal static IntPtr GetTemporaryStringPointerForNative( string str )
	{
		var f = new PassBackString( str );
		FrameAllocatedStrings.Add( f );
		return f.Pointer;
	}
}

internal class NativeAssemblyLoadException : Exception
{
	public int ErrorCode { get; }

	public NativeAssemblyLoadException( string message, int errorCode ) : base( message )
	{
		ErrorCode = errorCode;
	}

	public override string ToString()
	{
		return $"{base.ToString()}, ErrorCode: {ErrorCode}";
	}
}
