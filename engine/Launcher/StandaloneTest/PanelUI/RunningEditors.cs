using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Sandbox.LauncherUI;

/// <summary>
/// Which projects have an editor open right now. Found by asking the actual sbox-dev processes
/// what -project they were started with, so editors count no matter who started them.
/// </summary>
static class RunningEditors
{
	/// <summary>
	/// Full config file paths of every project an editor is currently open on.
	/// </summary>
	public static HashSet<string> Scan()
	{
		var running = new HashSet<string>( StringComparer.OrdinalIgnoreCase );

		foreach ( var process in Process.GetProcessesByName( "sbox-dev" ) )
		{
			try
			{
				var path = ProjectFromCommandLine( GetCommandLine( process ) );
				if ( path is not null ) running.Add( System.IO.Path.GetFullPath( path ) );
			}
			catch ( Exception )
			{
				// A process can die mid-question, or be one we're not allowed to ask about
			}
			finally
			{
				process.Dispose();
			}
		}

		return running;
	}

	/// <summary>
	/// The path handed to -project, from a full command line. The last one wins - the launcher
	/// forwards its own arguments, which can carry a -project of their own.
	/// </summary>
	static string ProjectFromCommandLine( string commandLine )
	{
		if ( commandLine is null ) return null;

		var index = commandLine.LastIndexOf( "-project", StringComparison.OrdinalIgnoreCase );
		if ( index < 0 ) return null;

		var rest = commandLine.Substring( index + "-project".Length ).TrimStart();
		if ( rest.Length == 0 ) return null;

		if ( rest[0] == '"' )
		{
			var end = rest.IndexOf( '"', 1 );
			return end > 1 ? rest.Substring( 1, end - 1 ) : null;
		}

		var space = rest.IndexOf( ' ' );
		return space > 0 ? rest.Substring( 0, space ) : rest;
	}

	static string GetCommandLine( Process process )
	{
		if ( OperatingSystem.IsWindows() ) return GetCommandLineWindows( process.Id );

		if ( OperatingSystem.IsLinux() ) return System.IO.File.ReadAllText( $"/proc/{process.Id}/cmdline" ).Replace( '\0', ' ' );

		return null;
	}

	//
	// Windows doesn't hand out other processes' command lines, but they're sitting in each
	// process at a known place: PEB -> ProcessParameters -> CommandLine. 64 bit layout.
	//

	const int PROCESS_QUERY_INFORMATION = 0x0400;
	const int PROCESS_VM_READ = 0x0010;

	[StructLayout( LayoutKind.Sequential )]
	struct PROCESS_BASIC_INFORMATION
	{
		public IntPtr Reserved1;
		public IntPtr PebBaseAddress;
		public IntPtr Reserved2_0;
		public IntPtr Reserved2_1;
		public IntPtr UniqueProcessId;
		public IntPtr Reserved3;
	}

	[DllImport( "ntdll.dll" )]
	static extern int NtQueryInformationProcess( IntPtr hProcess, int nClass, ref PROCESS_BASIC_INFORMATION info, int nSize, out int nReturnedSize );

	[DllImport( "kernel32.dll" )]
	static extern IntPtr OpenProcess( int nAccess, bool bInherit, int nProcessId );

	[DllImport( "kernel32.dll" )]
	static extern bool ReadProcessMemory( IntPtr hProcess, IntPtr pAddress, byte[] pBuffer, IntPtr nSize, out IntPtr nRead );

	[DllImport( "kernel32.dll" )]
	static extern bool CloseHandle( IntPtr handle );

	static string GetCommandLineWindows( int processId )
	{
		var handle = OpenProcess( PROCESS_QUERY_INFORMATION | PROCESS_VM_READ, false, processId );
		if ( handle == IntPtr.Zero ) return null;

		try
		{
			var info = new PROCESS_BASIC_INFORMATION();
			if ( NtQueryInformationProcess( handle, 0, ref info, Marshal.SizeOf<PROCESS_BASIC_INFORMATION>(), out _ ) != 0 ) return null;

			var processParameters = ReadPointer( handle, info.PebBaseAddress + 0x20 );
			if ( processParameters == IntPtr.Zero ) return null;

			// CommandLine is a UNICODE_STRING - length in bytes, then padding, then the buffer pointer
			var unicodeString = ReadBytes( handle, processParameters + 0x70, 16 );
			if ( unicodeString is null ) return null;

			var byteCount = BitConverter.ToUInt16( unicodeString, 0 );
			var buffer = (IntPtr)BitConverter.ToInt64( unicodeString, 8 );
			if ( buffer == IntPtr.Zero || byteCount == 0 ) return null;

			var text = ReadBytes( handle, buffer, byteCount );
			return text is null ? null : System.Text.Encoding.Unicode.GetString( text );
		}
		finally
		{
			CloseHandle( handle );
		}
	}

	static IntPtr ReadPointer( IntPtr handle, IntPtr address )
	{
		var bytes = ReadBytes( handle, address, 8 );
		return bytes is null ? IntPtr.Zero : (IntPtr)BitConverter.ToInt64( bytes, 0 );
	}

	static byte[] ReadBytes( IntPtr handle, IntPtr address, int count )
	{
		var buffer = new byte[count];
		if ( !ReadProcessMemory( handle, address, buffer, (IntPtr)count, out var read ) || (long)read != count ) return null;

		return buffer;
	}
}
