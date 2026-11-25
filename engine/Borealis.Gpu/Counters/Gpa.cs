using System.IO;
using System.Runtime.InteropServices;

namespace Borealis.Gpu;

/// <summary>
/// AMD's GPU Performance API (GPUPerfAPI, MIT) for Vulkan, loaded at run time from <c>GPUPerfAPIVK-x64.dll</c>: the calls
/// <see cref="GpuCounters"/> makes, as declared in its 4.x headers (<c>gpu_perf_api.h</c>, <c>gpu_perf_api_vk.h</c>).
/// </summary>
internal static unsafe class Gpa
{
	public const string LibraryName = "GPUPerfAPIVK-x64.dll";

	/// <summary>The device extensions GPA needs (<c>AMD_GPA_REQUIRED_DEVICE_EXTENSION_NAME_LIST</c>), and the one it can use.</summary>
	public static readonly string[] DeviceExtensions = ["VK_AMD_gpa_interface", "VK_AMD_shader_core_properties"];
	public const string OptionalDeviceExtension = "VK_AMD_shader_core_properties2";
	public const string InstanceExtension = "VK_KHR_get_physical_device_properties2";

	// GpaStatus
	public const int Ok = 0, ResultNotReady = 1;

	// GpaOpenContextBits
	public const uint OpenClockModeNone = 0x0008, OpenClockModePeak = 0x0010, OpenEnableHardwareCounters = 0x0080;

	// GpaDataType, GpaUsageType
	public const int DataFloat64 = 0, DataUint64 = 1;
	public enum Usage { Ratio, Percentage, Cycles, Milliseconds, Bytes, Items, Kilobytes, Nanoseconds }

	// GpaSessionSampleType, GpaCommandListType
	public const int SessionDiscrete = 0, CommandListPrimary = 1;

	// GpaHwGeneration
	public static string Generation( int generation ) => generation switch
	{
		7 => "GFX10 (RDNA)",
		8 => "GFX10.3 (RDNA2)",
		9 => "GFX11 (RDNA3)",
		13 => "GFX12 (RDNA4)",
		15 => "GFX11.5 (RDNA3.5)",
		_ => $"generation {generation}",
	};

	[StructLayout( LayoutKind.Sequential )]
	public struct VkContextOpenInfo
	{
		public nint Instance;
		public nint PhysicalDevice;
		public nint Device;
	}

	static nint library;
	public static string LoadedFrom { get; private set; }

	public static delegate* unmanaged[Cdecl]< uint, int > Initialize;
	public static delegate* unmanaged[Cdecl]< int > Destroy;
	public static delegate* unmanaged[Cdecl]< void*, uint, nint*, int > OpenContext;
	public static delegate* unmanaged[Cdecl]< nint, int > CloseContext;
	public static delegate* unmanaged[Cdecl]< nint, byte**, int > GetDeviceName;
	public static delegate* unmanaged[Cdecl]< nint, int*, int > GetDeviceGeneration;
	public static delegate* unmanaged[Cdecl]< nint, uint*, int > GetDeviceMaxWaveSlots;
	public static delegate* unmanaged[Cdecl]< nint, uint*, int > GetDeviceMaxVgprs;
	public static delegate* unmanaged[Cdecl]< nint, uint*, int > GetDeviceMaxLdsBytes;
	public static delegate* unmanaged[Cdecl]< nint, uint*, int > GetNumCounters;
	public static delegate* unmanaged[Cdecl]< nint, uint, byte**, int > GetCounterName;
	public static delegate* unmanaged[Cdecl]< nint, uint, byte**, int > GetCounterGroup;
	public static delegate* unmanaged[Cdecl]< nint, uint, byte**, int > GetCounterDescription;
	public static delegate* unmanaged[Cdecl]< nint, uint, int*, int > GetCounterDataType;
	public static delegate* unmanaged[Cdecl]< nint, uint, int*, int > GetCounterUsageType;
	public static delegate* unmanaged[Cdecl]< nint, uint, int*, int > GetCounterSampleType;
	public static delegate* unmanaged[Cdecl]< nint, int, nint*, int > CreateSession;
	public static delegate* unmanaged[Cdecl]< nint, int > DeleteSession;
	public static delegate* unmanaged[Cdecl]< nint, int > BeginSession;
	public static delegate* unmanaged[Cdecl]< nint, int > EndSession;
	public static delegate* unmanaged[Cdecl]< nint, int > AbortSession;
	public static delegate* unmanaged[Cdecl]< nint, uint, int > EnableCounter;
	public static delegate* unmanaged[Cdecl]< nint, uint*, int > GetPassCount;
	public static delegate* unmanaged[Cdecl]< nint, uint*, int > GetNumEnabledCounters;
	public static delegate* unmanaged[Cdecl]< nint, uint, uint*, int > GetEnabledIndex;
	public static delegate* unmanaged[Cdecl]< nint, uint, nint, int, nint*, int > BeginCommandList;
	public static delegate* unmanaged[Cdecl]< nint, int > EndCommandList;
	public static delegate* unmanaged[Cdecl]< uint, nint, int > BeginSample;
	public static delegate* unmanaged[Cdecl]< nint, int > EndSample;
	public static delegate* unmanaged[Cdecl]< nint, uint*, int > GetSampleCount;
	public static delegate* unmanaged[Cdecl]< nint, uint, uint*, int > GetSampleId;
	public static delegate* unmanaged[Cdecl]< nint, int > IsSessionComplete;
	public static delegate* unmanaged[Cdecl]< nint, uint, nuint*, int > GetSampleResultSize;
	public static delegate* unmanaged[Cdecl]< nint, uint, nuint, void*, int > GetSampleResult;
	public static delegate* unmanaged[Cdecl]< int, byte* > GetStatusAsStr;

	/// <summary>
	/// The folders searched for the library, in order: <c>BOREALIS_GPA_PATH</c> (a folder or the DLL), the process's own
	/// folder, then the copy RenderDoc ships in its AMD plugin.
	/// </summary>
	public static IEnumerable<string> Candidates()
	{
		if ( Environment.GetEnvironmentVariable( "BOREALIS_GPA_PATH" ) is { Length: > 0 } custom )
			yield return custom.EndsWith( ".dll", StringComparison.OrdinalIgnoreCase ) ? custom : Path.Combine( custom, LibraryName );
		yield return Path.Combine( AppContext.BaseDirectory, LibraryName );
		var programs = Environment.GetFolderPath( Environment.SpecialFolder.ProgramFiles );
		yield return Path.Combine( programs, "RenderDoc", "plugins", "amd", "counters", LibraryName );
	}

	/// <summary>Load the library once; the reason it couldn't, or null.</summary>
	public static string Load()
	{
		if ( library != 0 ) return null;
		if ( !OperatingSystem.IsWindows() ) return "GPUPerfAPI is loaded on Windows only";

		foreach ( var path in Candidates() )
		{
			if ( !File.Exists( path ) || !NativeLibrary.TryLoad( path, out var handle ) ) continue;
			library = handle;
			LoadedFrom = path;
			break;
		}

		if ( library == 0 ) return $"{LibraryName} wasn't found (set BOREALIS_GPA_PATH to its folder; RenderDoc ships one in plugins/amd/counters)";

		static nint Get( string name ) => NativeLibrary.GetExport( library, name );
		Initialize = (delegate* unmanaged[Cdecl]< uint, int >)Get( "GpaInitialize" );
		Destroy = (delegate* unmanaged[Cdecl]< int >)Get( "GpaDestroy" );
		OpenContext = (delegate* unmanaged[Cdecl]< void*, uint, nint*, int >)Get( "GpaOpenContext" );
		CloseContext = (delegate* unmanaged[Cdecl]< nint, int >)Get( "GpaCloseContext" );
		GetDeviceName = (delegate* unmanaged[Cdecl]< nint, byte**, int >)Get( "GpaGetDeviceName" );
		GetDeviceGeneration = (delegate* unmanaged[Cdecl]< nint, int*, int >)Get( "GpaGetDeviceGeneration" );
		GetDeviceMaxWaveSlots = (delegate* unmanaged[Cdecl]< nint, uint*, int >)Get( "GpaGetDeviceMaxWaveSlots" );
		GetDeviceMaxVgprs = (delegate* unmanaged[Cdecl]< nint, uint*, int >)Get( "GpaGetDeviceMaxVgprs" );
		GetDeviceMaxLdsBytes = (delegate* unmanaged[Cdecl]< nint, uint*, int >)Get( "GpaGetDeviceMaxLdsBytes" );
		GetNumCounters = (delegate* unmanaged[Cdecl]< nint, uint*, int >)Get( "GpaGetNumCounters" );
		GetCounterName = (delegate* unmanaged[Cdecl]< nint, uint, byte**, int >)Get( "GpaGetCounterName" );
		GetCounterGroup = (delegate* unmanaged[Cdecl]< nint, uint, byte**, int >)Get( "GpaGetCounterGroup" );
		GetCounterDescription = (delegate* unmanaged[Cdecl]< nint, uint, byte**, int >)Get( "GpaGetCounterDescription" );
		GetCounterDataType = (delegate* unmanaged[Cdecl]< nint, uint, int*, int >)Get( "GpaGetCounterDataType" );
		GetCounterUsageType = (delegate* unmanaged[Cdecl]< nint, uint, int*, int >)Get( "GpaGetCounterUsageType" );
		GetCounterSampleType = (delegate* unmanaged[Cdecl]< nint, uint, int*, int >)Get( "GpaGetCounterSampleType" );
		CreateSession = (delegate* unmanaged[Cdecl]< nint, int, nint*, int >)Get( "GpaCreateSession" );
		DeleteSession = (delegate* unmanaged[Cdecl]< nint, int >)Get( "GpaDeleteSession" );
		BeginSession = (delegate* unmanaged[Cdecl]< nint, int >)Get( "GpaBeginSession" );
		EndSession = (delegate* unmanaged[Cdecl]< nint, int >)Get( "GpaEndSession" );
		AbortSession = (delegate* unmanaged[Cdecl]< nint, int >)Get( "GpaAbortSession" );
		EnableCounter = (delegate* unmanaged[Cdecl]< nint, uint, int >)Get( "GpaEnableCounter" );
		GetPassCount = (delegate* unmanaged[Cdecl]< nint, uint*, int >)Get( "GpaGetPassCount" );
		GetNumEnabledCounters = (delegate* unmanaged[Cdecl]< nint, uint*, int >)Get( "GpaGetNumEnabledCounters" );
		GetEnabledIndex = (delegate* unmanaged[Cdecl]< nint, uint, uint*, int >)Get( "GpaGetEnabledIndex" );
		BeginCommandList = (delegate* unmanaged[Cdecl]< nint, uint, nint, int, nint*, int >)Get( "GpaBeginCommandList" );
		EndCommandList = (delegate* unmanaged[Cdecl]< nint, int >)Get( "GpaEndCommandList" );
		BeginSample = (delegate* unmanaged[Cdecl]< uint, nint, int >)Get( "GpaBeginSample" );
		EndSample = (delegate* unmanaged[Cdecl]< nint, int >)Get( "GpaEndSample" );
		GetSampleCount = (delegate* unmanaged[Cdecl]< nint, uint*, int >)Get( "GpaGetSampleCount" );
		GetSampleId = (delegate* unmanaged[Cdecl]< nint, uint, uint*, int >)Get( "GpaGetSampleId" );
		IsSessionComplete = (delegate* unmanaged[Cdecl]< nint, int >)Get( "GpaIsSessionComplete" );
		GetSampleResultSize = (delegate* unmanaged[Cdecl]< nint, uint, nuint*, int >)Get( "GpaGetSampleResultSize" );
		GetSampleResult = (delegate* unmanaged[Cdecl]< nint, uint, nuint, void*, int >)Get( "GpaGetSampleResult" );
		GetStatusAsStr = (delegate* unmanaged[Cdecl]< int, byte* >)Get( "GpaGetStatusAsStr" );
		return null;
	}

	public static string Text( byte* text ) => text is null ? "" : Marshal.PtrToStringUTF8( (nint)text ) ?? "";

	public static string Status( int status ) => GetStatusAsStr is null ? status.ToString() : $"{Text( GetStatusAsStr( status ) )} ({status})";
}
