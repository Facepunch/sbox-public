using Borealis.Gpu.Vulkan;
using System.Diagnostics;
using System.Threading;

namespace Borealis.Gpu;

public sealed unsafe partial class Device
{
	// The thread frames begin on, what it's doing in the device (a constant name, or null outside it) and since when
	int frameThread;
	string doing;
	long doingSince;

	// When the current frame began (BeginFrame), and what the hang watch noted while it went on too long
	long frameBegan;

	// The allocator's driver calls and reserved bytes when the frame began, for what the watch says went on during it
	(long Allocations, long Frees, ulong Reserved) frameMemory;
	volatile string stallNote;
	Thread watchThread;
	volatile bool watching;

	/// <summary>A frame lasting longer than this is noted by the hang watch (<see cref="DeviceOptions.HangWatch"/>).</summary>
	internal const double StallSeconds = 1;

	/// <summary>Frames that lasted longer than <see cref="StallSeconds"/> while the hang watch ran.</summary>
	public int FrameStalls { get; private set; }

	/// <summary>
	/// Note that the frame thread is in <paramref name="what"/> (a constant) until the scope ends, for the hang watch: a
	/// frame that stops says where its thread was. Nothing on other threads, or without the watch.
	/// </summary>
	internal Doing Enter( string what )
	{
		// A profiler zone of the same name, on any thread
		var profiler = Profiler;
		profiler?.BeginZone( ZoneName( what ), 0x8080FFFF );

		if ( watchThread is null || Environment.CurrentManagedThreadId != frameThread ) return new Doing( null, null, profiler );
		var outer = doing;
		doing = what;
		Volatile.Write( ref doingSince, Stopwatch.GetTimestamp() );
		return new Doing( this, outer, profiler );
	}

	internal readonly struct Doing( Device device, string outer, IDeviceProfiler profiler ) : IDisposable
	{
		public void Dispose()
		{
			profiler?.EndZone();
			if ( device is null ) return;
			device.doing = outer;
			Volatile.Write( ref device.doingSince, Stopwatch.GetTimestamp() );
		}
	}

	// UTF-8 copies of Enter's names (constants) for profiler zones, made once each and kept for the process
	static readonly System.Collections.Concurrent.ConcurrentDictionary<string, nint> zoneNames = new( ReferenceEqualityComparer.Instance );

	static nint ZoneName( string what ) => zoneNames.TryGetValue( what, out var name ) ? name : zoneNames.GetOrAdd( what, static w => System.Runtime.InteropServices.Marshal.StringToCoTaskMemUTF8( "Borealis.Gpu " + w ) );

	void StartWatch()
	{
		watching = true;
		watchThread = new Thread( Watch ) { IsBackground = true, Name = "Borealis.Gpu hang watch", Priority = ThreadPriority.AboveNormal };
		watchThread.Start();
	}

	void StopWatch()
	{
		watching = false;
		watchThread?.Join();
	}

	/// <summary>
	/// At the start of each frame (<see cref="BeginFrame"/>): say how long the last one went on when the watch noted it, and
	/// time this one from here.
	/// </summary>
	void WatchFrame()
	{
		if ( watchThread is null ) return;
		frameThread = Environment.CurrentManagedThreadId;
		var now = Stopwatch.GetTimestamp();
		var began = Volatile.Read( ref frameBegan );
		frameMemory = (Allocator.DriverAllocations, Allocator.DriverFrees, Allocator.Reserved);
		Volatile.Write( ref frameBegan, now );
		if ( stallNote is not { } note || began == 0 ) return;

		stallNote = null;
		FrameStalls++;
		var seconds = Stopwatch.GetElapsedTime( began, now ).TotalSeconds;
		RememberEvent( "watch: the frame ended, ms:", (long)(seconds * 1000) );
		Warn( "Borealis.Gpu.FrameStall", $"Frame {FrameNumber} took {seconds:F1} s; {note}" );
	}

	/// <summary>
	/// The hang watch's thread: while a frame goes on past <see cref="StallSeconds"/>, and again each time it doubles, note
	/// where the frame thread is (the device call it's in, or outside the device) and how far each queue's timeline has got,
	/// both among a loss report's events and for the frame's warning when it ends. A GPU timeout shows as the frame thread
	/// stopped in a call the driver holds while our queues have finished all they were given, or as a queue that stopped.
	/// </summary>
	void Watch()
	{
		var next = StallSeconds;
		long noted = 0;
		while ( watching )
		{
			Thread.Sleep( 50 );
			var began = Volatile.Read( ref frameBegan );
			if ( began == 0 || IsLost ) continue;
			if ( began != noted ) next = StallSeconds;

			var stalled = Stopwatch.GetElapsedTime( began ).TotalSeconds;
			if ( stalled < next ) continue;
			noted = began;
			next *= 2;

			var what = doing;
			var since = Stopwatch.GetElapsedTime( Volatile.Read( ref doingSince ) ).TotalMilliseconds;
			var note = $"frame thread {(what is null ? "outside the device" : $"in {what}")} for {since:F0} ms; {DescribeTimeline( graphicsQueue )}"
				+ (computeQueue != graphicsQueue ? $", {DescribeTimeline( computeQueue )}" : "")
				+ $"; since the frame began {Allocator.DriverAllocations - frameMemory.Allocations} driver allocations and {Allocator.DriverFrees - frameMemory.Frees} frees, reserved {frameMemory.Reserved >> 20} -> {Allocator.Reserved >> 20} MiB";
			RememberEvent( $"watch: {note}; frame going on, ms:", (long)(stalled * 1000) );
			stallNote = $"at {stalled:F1} s: {note}";
		}
	}

	/// <summary>A queue's timeline read now, without taking the device as lost (that's left to the frame thread's calls).</summary>
	string DescribeTimeline( DeviceQueue queue )
	{
		ulong value;
		var result = Vk.vkGetSemaphoreCounterValue( Vk.Device, queue.Timeline, &value );
		var reached = result == VkResult.Success ? value.ToString() : result.ToString();
		return $"{queue.Type} reached {reached} of {Volatile.Read( ref queue.Flushed )} flushed ({queue.Submitted} submitted)";
	}
}
