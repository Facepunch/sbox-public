using Microsoft.Win32.SafeHandles;
using System;
using System.Collections.Generic;
using System.Linq;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Sandbox.Cli;

/// <summary>
/// Owns a launched instance. A non-inherited Windows job handle guarantees cleanup even
/// when the CLI is killed. Job membership is assigned atomically during process creation.
/// </summary>
internal sealed class OwnedProcess : IDisposable
{
	readonly SafeFileHandle job;
	internal Process Process { get; }

	internal OwnedProcess( string executable, string directory, IReadOnlyList<string> arguments )
	{
		if ( !OperatingSystem.IsWindows() )
			throw new PlatformNotSupportedException( "Launching owned instances currently requires Windows. Use --connect for an existing instance." );

		job = CreateJobObject( IntPtr.Zero, null );
		if ( job.IsInvalid )
			throw new Win32Exception();

		var limits = new JobLimits { Basic = new BasicLimits { LimitFlags = 0x2000 } };
		var info = new ProcessInformation();
		IntPtr attributes = IntPtr.Zero;
		IntPtr jobValue = IntPtr.Zero;
		bool attributesInitialized = false;

		try
		{
			if ( !SetInformationJobObject( job, 9, ref limits, Marshal.SizeOf<JobLimits>() ) )
				throw new Win32Exception();

			UIntPtr size = UIntPtr.Zero;
			InitializeProcThreadAttributeList( IntPtr.Zero, 1, 0, ref size );
			attributes = Marshal.AllocHGlobal( checked((int)size.ToUInt64()) );
			if ( !InitializeProcThreadAttributeList( attributes, 1, 0, ref size ) )
				throw new Win32Exception();

			attributesInitialized = true;
			jobValue = Marshal.AllocHGlobal( IntPtr.Size );
			Marshal.WriteIntPtr( jobValue, job.DangerousGetHandle() );

			// PROC_THREAD_ATTRIBUTE_JOB_LIST closes the create-then-assign race: even if
			// this CLI dies inside CreateProcess, Windows owns and cleans up the child.
			if ( !UpdateProcThreadAttribute( attributes, 0, (UIntPtr)0x0002000d,
				jobValue, (UIntPtr)IntPtr.Size, IntPtr.Zero, IntPtr.Zero ) )
				throw new Win32Exception();

			var startup = new StartupInfoEx
			{
				Startup = new StartupInfo { Size = Marshal.SizeOf<StartupInfoEx>() },
				Attributes = attributes
			};
			var command = new StringBuilder( string.Join( " ", new[] { executable }.Concat( arguments ).Select( QuoteArgument ) ) );

			// No inherited stdio handles: engine output must never corrupt the MCP stream or
			// keep the client's pipe open after this bridge exits. Engine logs remain on disk.
			if ( !CreateProcess( executable, command, IntPtr.Zero, IntPtr.Zero, false,
				0x00000004 | 0x08000000 | 0x00080000, IntPtr.Zero, directory, ref startup, out info ) )
				throw new Win32Exception();

			Process = Process.GetProcessById( info.ProcessId );

			if ( ResumeThread( info.Thread ) == uint.MaxValue )
				throw new Win32Exception();
		}
		catch
		{
			if ( info.Process != IntPtr.Zero )
				TerminateProcess( info.Process, 1 );

			Process?.Dispose();
			job.Dispose();
			throw;
		}
		finally
		{
			if ( info.Thread != IntPtr.Zero ) CloseHandle( info.Thread );
			if ( info.Process != IntPtr.Zero ) CloseHandle( info.Process );
			if ( attributesInitialized ) DeleteProcThreadAttributeList( attributes );
			if ( attributes != IntPtr.Zero ) Marshal.FreeHGlobal( attributes );
			if ( jobValue != IntPtr.Zero ) Marshal.FreeHGlobal( jobValue );
			GC.KeepAlive( job );
		}
	}

	internal static string QuoteArgument( string argument )
	{
		// Windows CommandLineToArgvW rules: double backslashes before a quote or the end.
		var result = new StringBuilder( "\"" );
		int slashes = 0;
		foreach ( var character in argument )
		{
			if ( character == '\\' )
			{
				slashes++;
				continue;
			}

			result.Append( '\\', character == '"' ? slashes * 2 + 1 : slashes );
			result.Append( character );
			slashes = 0;
		}

		return result.Append( '\\', slashes * 2 ).Append( '"' ).ToString();
	}

	/// <summary>
	/// Kill only the owned process tree and release its handles.
	/// </summary>
	public void Dispose()
	{
		job.Dispose();
		Process.Dispose();
	}

	[StructLayout( LayoutKind.Sequential )]
	struct BasicLimits
	{
		internal long ProcessTime, JobTime;
		internal uint LimitFlags;
		internal UIntPtr MinimumWorkingSet, MaximumWorkingSet;
		internal uint ActiveProcessLimit;
		internal UIntPtr Affinity;
		internal uint PriorityClass, SchedulingClass;
	}

	[StructLayout( LayoutKind.Sequential )]
	struct JobLimits
	{
		internal BasicLimits Basic;
		internal ulong ReadOperations, WriteOperations, OtherOperations, ReadBytes, WriteBytes, OtherBytes;
		internal UIntPtr ProcessMemoryLimit, JobMemoryLimit, PeakProcessMemory, PeakJobMemory;
	}

	[StructLayout( LayoutKind.Sequential, CharSet = CharSet.Unicode )]
	struct StartupInfo
	{
		internal int Size;
		internal string Reserved, Desktop, Title;
		internal int X, Y, XSize, YSize, XCountChars, YCountChars, FillAttribute, Flags;
		internal short ShowWindow, ReservedSize;
		internal IntPtr ReservedPointer, Input, Output, Error;
	}

	[StructLayout( LayoutKind.Sequential )]
	struct ProcessInformation
	{
		internal IntPtr Process, Thread;
		internal int ProcessId, ThreadId;
	}

	[StructLayout( LayoutKind.Sequential )]
	struct StartupInfoEx
	{
		internal StartupInfo Startup;
		internal IntPtr Attributes;
	}

	[DllImport( "kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true )]
	static extern SafeFileHandle CreateJobObject( IntPtr attributes, string name );

	[DllImport( "kernel32.dll", SetLastError = true )]
	static extern bool SetInformationJobObject( SafeFileHandle job, int infoClass, ref JobLimits info, int size );

	[DllImport( "kernel32.dll", SetLastError = true )]
	static extern bool InitializeProcThreadAttributeList( IntPtr list, int count, uint flags, ref UIntPtr size );

	[DllImport( "kernel32.dll", SetLastError = true )]
	static extern bool UpdateProcThreadAttribute( IntPtr list, uint flags, UIntPtr attribute,
		IntPtr value, UIntPtr size, IntPtr previous, IntPtr returnedSize );

	[DllImport( "kernel32.dll" )]
	static extern void DeleteProcThreadAttributeList( IntPtr list );

	[DllImport( "kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true )]
	static extern bool CreateProcess( string application, StringBuilder commandLine, IntPtr processAttributes,
		IntPtr threadAttributes, bool inheritHandles, uint flags, IntPtr environment, string directory,
		ref StartupInfoEx startup, out ProcessInformation info );

	[DllImport( "kernel32.dll", SetLastError = true )]
	static extern uint ResumeThread( IntPtr thread );

	[DllImport( "kernel32.dll" )]
	static extern bool TerminateProcess( IntPtr process, uint exitCode );

	[DllImport( "kernel32.dll" )]
	static extern bool CloseHandle( IntPtr handle );
}
