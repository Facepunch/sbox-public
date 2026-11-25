using System.Linq;

namespace Borealis.Gpu;

public sealed partial class Device
{
	// Whether GPUPerfAPI was initialised for this device (DeviceOptions.GpuCounters), so its extensions go on
	readonly bool countersPrepared;

	/// <summary>
	/// Hardware performance counters per GPU scope, where the device was made with <see cref="DeviceOptions.GpuCounters"/>
	/// and GPUPerfAPI could open it; null otherwise.
	/// </summary>
	public GpuCounters Counters { get; private set; }

	void AddCounterExtensions( List<string> extensions )
	{
		var missing = Gpa.DeviceExtensions.Where( e => !deviceExtensions.Contains( e ) ).ToList();
		if ( missing.Count > 0 )
		{
			message?.Invoke( new DeviceMessage( MessageSeverity.Warning, "Borealis.Gpu.Counters", $"GPU counters are off: {Adapter.Name} has no {string.Join( " or ", missing )}" ) );
			return;
		}

		foreach ( var name in Gpa.DeviceExtensions ) if ( !extensions.Contains( name ) ) extensions.Add( name );
		if ( deviceExtensions.Contains( Gpa.OptionalDeviceExtension ) && !extensions.Contains( Gpa.OptionalDeviceExtension ) ) extensions.Add( Gpa.OptionalDeviceExtension );
	}

	void OpenCounters( string selection )
	{
		if ( Gpa.DeviceExtensions.Any( e => !deviceExtensions.Contains( e ) ) ) return;
		void Note( string text ) => message?.Invoke( new DeviceMessage( MessageSeverity.Warning, "Borealis.Gpu.Counters", text ) );
		Counters = GpuCounters.Open( this, selection, Instance.Instance, PhysicalDevice, Vk.Device, Note );
		if ( Counters is null ) return;

		var (name, generation, waves, vgprs, lds) = Counters.Hardware;
		message?.Invoke( new DeviceMessage( MessageSeverity.Info, "Borealis.Gpu.Counters",
			$"GPU counters on {name}, {generation} ({waves} wave slots, {vgprs} VGPRs, {lds >> 10} KiB LDS), GPUPerfAPI from {Counters.Library}: " +
			$"{Counters.Counters.Count} counters in {Counters.PassCount} passes" +
			(Counters.Missing.Count > 0 ? $"; not on this GPU: {string.Join( ", ", Counters.Missing )}" : "") ) );
	}
}
