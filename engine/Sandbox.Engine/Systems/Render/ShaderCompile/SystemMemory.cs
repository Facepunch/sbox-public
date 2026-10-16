using System.Runtime.InteropServices;

namespace Sandbox.Engine.Shaders;

/// <summary>
/// Physical memory the machine has free right now, not what this process could use. Build boxes run
/// several jobs side by side, so the total says little about what a compile can take.
/// </summary>
static class SystemMemory
{
	/// <summary>
	/// Free physical memory in bytes, or null if it can't be read on this platform.
	/// </summary>
	public static long? AvailablePhysicalBytes()
	{
		try
		{
			if ( OperatingSystem.IsWindows() )
			{
				var status = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
				return GlobalMemoryStatusEx( ref status ) ? (long)status.AvailPhys : null;
			}

			if ( OperatingSystem.IsLinux() )
			{
				foreach ( var line in System.IO.File.ReadLines( "/proc/meminfo" ) )
				{
					// "MemAvailable:   12345678 kB"
					if ( !line.StartsWith( "MemAvailable:" ) ) continue;

					var parts = line.Split( ' ', StringSplitOptions.RemoveEmptyEntries );
					return long.TryParse( parts[1], out var kb ) ? kb * 1024 : null;
				}
			}
		}
		catch ( Exception )
		{
			// Fall through, the caller picks a default
		}

		return null;
	}

	[StructLayout( LayoutKind.Sequential )]
	struct MemoryStatusEx
	{
		public uint Length;
		public uint MemoryLoad;
		public ulong TotalPhys;
		public ulong AvailPhys;
		public ulong TotalPageFile;
		public ulong AvailPageFile;
		public ulong TotalVirtual;
		public ulong AvailVirtual;
		public ulong AvailExtendedVirtual;
	}

	[DllImport( "kernel32.dll", SetLastError = true )]
	[return: MarshalAs( UnmanagedType.Bool )]
	static extern bool GlobalMemoryStatusEx( ref MemoryStatusEx buffer );
}
