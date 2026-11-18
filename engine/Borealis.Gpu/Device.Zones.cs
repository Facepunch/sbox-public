using System.Runtime.InteropServices;

namespace Borealis.Gpu;

/// <summary>
/// Begins and ends profiler zones (Tracy in the engine): a zone's name is a UTF-8 string that lives for the process, its colour
/// 0xRRGGBBAA. What <see cref="Device.Profiler"/> takes.
/// </summary>
public interface IDeviceProfiler
{
	/// <summary>Begin a zone on the calling thread.</summary>
	void BeginZone( nint name, uint color );

	/// <summary>End the calling thread's innermost zone.</summary>
	void EndZone();
}

public sealed partial class Device
{
	/// <summary>
	/// Profiler zones around the device's slow calls (shader module and pipeline creation), on whichever thread makes them; null
	/// for none.
	/// </summary>
	public IDeviceProfiler Profiler { get; set; }

	static readonly nint ZonePipeline = Marshal.StringToCoTaskMemUTF8( "Borealis.Gpu Create Pipeline" );
	static readonly nint ZoneShaderModule = Marshal.StringToCoTaskMemUTF8( "Borealis.Gpu Create Shader Module" );
	static readonly nint ZoneAllocateMemory = Marshal.StringToCoTaskMemUTF8( "Borealis.Gpu Allocate Memory" );

	/// <summary>A zone around a driver memory allocation (<c>vkAllocateMemory</c>), for <see cref="Memory.MemoryAllocator"/>.</summary>
	internal ZoneScope AllocateMemoryZone() => Zone( ZoneAllocateMemory );

	ZoneScope Zone( nint name )
	{
		var profiler = Profiler;
		profiler?.BeginZone( name, 0xFF8040FF );
		return new ZoneScope( profiler );
	}

	internal readonly struct ZoneScope( IDeviceProfiler profiler ) : IDisposable
	{
		public void Dispose() => profiler?.EndZone();
	}
}
