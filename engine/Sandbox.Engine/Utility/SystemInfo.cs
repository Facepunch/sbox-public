using System.IO;

namespace Sandbox.Engine;

public static class SystemInfo
{
	static DriveInfo _driveInfo;

	static SystemInfo()
	{
		UpdateDriveInfo();
	}

	// I am worried about this taking like 4 seconds to run or something
	static void UpdateDriveInfo()
	{
		_driveInfo = new DriveInfo( Path.GetPathRoot( System.Environment.CurrentDirectory ) );
		StorageSizeAvailable = _driveInfo.AvailableFreeSpace;
		StorageSizeTotal = _driveInfo.TotalSize;
	}

	/// <summary>
	/// Human-readable product name of this system's processor.
	/// </summary>
	public static string ProcessorName { get; private set; } = "unset";

	/// <summary>
	/// The frequency of this system's processor in GHz.
	/// </summary>
	public static float ProcessorFrequency { get; private set; } = 0;

	/// <summary>
	/// The number of logical processors in this system.
	/// </summary>
	public static float ProcessorCount { get; private set; } = 0;

	/// <summary>
	/// Total physical memory available on this machine, in bytes.
	/// </summary>
	public static ulong TotalMemory { get; private set; } = 0;

	/// <summary>
	/// Human-readable product name of the graphics card in this system.
	/// </summary>
	public static string Gpu { get; private set; } = "unset";

	/// <summary>
	/// The version number of the graphics card driver.
	/// </summary>
	public static string GpuVersion { get; private set; } = "unset";

	/// <summary>
	/// Total VRAM on this system's graphics card.
	/// </summary>
	public static ulong GpuMemory { get; private set; } = 0;

	internal static void Set( string cpu, ushort processorCount, ulong frequency, ulong memory )
	{
		ProcessorName = cpu;
		ProcessorFrequency = frequency / 1000000000.0f; // cycles/sec -> GHz
		ProcessorCount = processorCount;
		TotalMemory = memory;
	}

	internal static void SetGpu( string driver, string version, ulong memory )
	{
		Gpu = driver;
		GpuVersion = version;
		GpuMemory = memory;

		// Main thread with the window up, so the SDL display queries run here rather than on whichever thread first reports
		_ = GraphicsEnv.Value;
	}

	/// <summary>
	/// Indicates the amount of available free space on game drive in bytes
	/// </summary>
	public static long StorageSizeAvailable { get; private set; }

	/// <summary>
	/// Gets the total size of storage space on game drive in bytes
	/// </summary>
	public static long StorageSizeTotal { get; private set; }

	/// <summary>
	/// Operating system version including the marketing build name, e.g. "10.0.26200 (25H2)".
	/// </summary>
	public static string OsVersion => GraphicsEnv.Value.OsVersion;

	/// <summary>
	/// Windows only. Hardware accelerated GPU scheduling as configured; a change only applies after a reboot.
	/// Null when the key is missing, meaning the OS or GPU does not support it.
	/// </summary>
	public static bool? WinHags => GraphicsEnv.Value.WinHags;

	/// <summary>
	/// Windows only. The "Variable refresh rate" toggle for windowed games. Null when left at its default.
	/// </summary>
	public static bool? WinVrr => GraphicsEnv.Value.WinVrr;

	/// <summary>
	/// Windows only. The "Optimizations for windowed games" toggle. Null when left at its default.
	/// </summary>
	public static bool? WinWindowedOptimizations => GraphicsEnv.Value.WinWindowedOptimizations;

	/// <summary>
	/// Windows only. Whether fullscreen optimizations were disabled for this executable in its compatibility settings.
	/// </summary>
	public static bool WinFullscreenOptimizationsDisabled => GraphicsEnv.Value.WinFullscreenOptimizationsDisabled;

	/// <summary>
	/// Windows only. Game Mode. Null when left at its default.
	/// </summary>
	public static bool? WinGameMode => GraphicsEnv.Value.WinGameMode;

	/// <summary>
	/// Number of monitors attached.
	/// </summary>
	public static int MonitorCount => GraphicsEnv.Value.MonitorCount;

	/// <summary>
	/// Refresh rate of the default monitor in Hz, 0 if unknown.
	/// </summary>
	public static int DisplayRefreshRate => GraphicsEnv.Value.DisplayRefreshRate;

	record struct GraphicsEnvironment( string OsVersion, bool? WinHags, bool? WinVrr, bool? WinWindowedOptimizations, bool WinFullscreenOptimizationsDisabled, bool? WinGameMode, int MonitorCount, int DisplayRefreshRate );

	// Read once on first use. Everything here is best effort and must never throw at a call site.
	static readonly Lazy<GraphicsEnvironment> GraphicsEnv = new( ReadGraphicsEnvironment );

	static GraphicsEnvironment ReadGraphicsEnvironment()
	{
		var osVersion = System.Environment.OSVersion.Version;
		var env = new GraphicsEnvironment( osVersion.Build >= 0 ? osVersion.ToString( 3 ) : osVersion.ToString(), null, null, null, false, null, 0, 0 );

		try
		{
			int w = 0, h = 0;
			uint hz = 0;
			env.MonitorCount = NativeEngine.EngineGlobal.Plat_GetMonitorCount();

			if ( NativeEngine.EngineGlobal.Plat_GetDesktopResolution( NativeEngine.EngineGlobal.Plat_GetDefaultMonitorIndex(), ref w, ref h, ref hz ) )
				env.DisplayRefreshRate = (int)hz;
		}
		catch ( Exception e )
		{
			Log.Warning( $"SystemInfo: reading display information failed: {e.Message}" );
		}

		if ( !OperatingSystem.IsWindows() )
			return env;

		try
		{
			var displayVersion = Microsoft.Win32.Registry.GetValue( @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion", "DisplayVersion", null ) as string;
			if ( !string.IsNullOrEmpty( displayVersion ) )
				env.OsVersion = $"{env.OsVersion} ({displayVersion})";

			// 2 = on, 1 = off
			if ( Microsoft.Win32.Registry.GetValue( @"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "HwSchMode", null ) is int hwSchMode )
				env.WinHags = hwSchMode == 2;

			// e.g. "VRROptimizeEnable=1;SwapEffectUpgradeEnable=0;", each part absent until the user touches it
			if ( Microsoft.Win32.Registry.GetValue( @"HKEY_CURRENT_USER\Software\Microsoft\DirectX\UserGpuPreferences", "DirectXUserGlobalSettings", null ) is string dxPrefs )
			{
				env.WinVrr = ReadFlag( dxPrefs, "VRROptimizeEnable" );
				env.WinWindowedOptimizations = ReadFlag( dxPrefs, "SwapEffectUpgradeEnable" );
			}

			if ( Microsoft.Win32.Registry.GetValue( @"HKEY_CURRENT_USER\Software\Microsoft\GameBar", "AutoGameModeEnabled", null ) is int gameMode )
				env.WinGameMode = gameMode != 0;

			var exe = System.Environment.ProcessPath;
			if ( !string.IsNullOrEmpty( exe ) && Microsoft.Win32.Registry.GetValue( @"HKEY_CURRENT_USER\Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers", exe, null ) is string layers )
				env.WinFullscreenOptimizationsDisabled = layers.Contains( "DISABLEDXMAXIMIZEDWINDOWEDMODE", StringComparison.OrdinalIgnoreCase );
		}
		catch ( Exception e )
		{
			Log.Warning( $"SystemInfo: reading the Windows graphics settings failed: {e.Message}" );
		}

		return env;
	}

	static bool? ReadFlag( string settings, string name )
	{
		foreach ( var part in settings.Split( ';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries ) )
		{
			var eq = part.IndexOf( '=' );
			if ( eq > 0 && part.AsSpan( 0, eq ).Equals( name, StringComparison.OrdinalIgnoreCase ) )
				return part.AsSpan( eq + 1 ).Trim().Equals( "1", StringComparison.Ordinal );
		}

		return null;
	}

	/// <summary>
	/// Return as an object, for sending to backends
	/// </summary>
	internal static object AsObject()
	{
		return new
		{
			SystemInfo.ProcessorName,
			SystemInfo.ProcessorCount,
			SystemInfo.ProcessorFrequency,
			SystemInfo.Gpu,
			SystemInfo.GpuVersion,
			GpuMb = SystemInfo.GpuMemory / (1024 * 1024),
			RamMb = SystemInfo.TotalMemory / (1024 * 1024),
			StorageSizeAvailable,
			StorageSizeTotal,
			OsVersion,
			MonitorCount,
			DisplayRefreshRate,
			WinHags,
			WinVrr,
			WinWindowedOptimizations,
			WinFullscreenOptimizationsDisabled,
			WinGameMode
		};
	}
}
