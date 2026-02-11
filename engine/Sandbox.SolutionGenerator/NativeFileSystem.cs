using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Sandbox.SolutionGenerator;

/// <summary>
/// Provides native file system operations with platform-specific implementations.
/// </summary>
internal static partial class NativeFileSystem
{
	[DllImport( "kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true )]
	private static extern IntPtr CreateFileW( string lpFileName, uint dwDesiredAccess, uint dwShareMode, IntPtr lpSecurityAttributes, uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile );

	[DllImport( "kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true )]
	private static extern uint GetFinalPathNameByHandleW( IntPtr hFile, char[] lpszFilePath, uint cchFilePath, uint dwFlags );

	[DllImport( "kernel32.dll", SetLastError = true )]
	private static extern bool CloseHandle( IntPtr hObject );

	private const uint FILE_FLAG_BACKUP_SEMANTICS = 0x02000000;
	private const uint OPEN_EXISTING = 3;
	private const uint FILE_SHARE_READ = 1;
	private const uint FILE_SHARE_WRITE = 2;

	/// <summary>
	/// Gets the canonical path with proper casing for the given path.
	/// On Windows, this resolves the true filesystem path including correct casing.
	/// On Linux, this returns the path unchanged since the filesystem is case-sensitive.
	/// </summary>
	/// <param name="path">The path to canonicalize.</param>
	/// <returns>The canonical path, or the original path if canonicalization fails.</returns>
	internal static string GetCanonicalPath( string path )
	{
		if ( string.IsNullOrWhiteSpace( path ) || !Path.IsPathRooted( path ) )
			return path;

		if ( !OperatingSystem.IsWindows() )
			return path;

		try
		{
			var handle = CreateFileW( path, 0, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, FILE_FLAG_BACKUP_SEMANTICS, IntPtr.Zero );
			if ( handle == IntPtr.Zero || handle == new IntPtr( -1 ) )
				return path;

			try
			{
				var buffer = new char[512];
				var len = GetFinalPathNameByHandleW( handle, buffer, (uint)buffer.Length, 0 );
				if ( len > 0 && len < buffer.Length )
				{
					var finalPath = new string( buffer, 0, (int)len );
					// Remove the \\?\ prefix added by Windows API
					if ( finalPath.StartsWith( @"\\?\" ) )
					{
						finalPath = finalPath.Substring( 4 );
					}
					return finalPath;
				}
				else
				{
					return path;
				}
			}
			finally
			{
				CloseHandle( handle );
			}
		}
		catch
		{
			// Ignore errors and return original path
			return path;
		}
	}
}
