using Borealis.Gpu;
using System.Threading;

namespace Borealis.Backend;

/// <summary>
/// GPU time and pipeline statistics per named scope: Borealis's layers and setup (its GPU scopes), each command list, and
/// whatever the caller times itself. Each scope is a timestamp at either end, written once everything before it on the
/// queue has finished, so scopes in submission order add up to the frame and time between them is time between lists.
/// For benchmarks: set <see cref="BackendDevice.PassTimer"/>, call <see cref="BeginFrame"/> before each frame and read a
/// frame back with <see cref="TryRead"/> once the GPU has finished it (<see cref="Slots"/> frames are kept).
/// </summary>
/// <remarks>
/// With <see cref="Statistics"/> on, each scope also counts the primitives, vertex and fragment shader invocations of
/// its work, which needs a rendering scope to close at either end of it: the frame records differently, so time it with
/// statistics off.
/// </remarks>
internal sealed class PassTimer : IDisposable
{
	/// <summary>Frames kept at once.</summary>
	public const int Slots = 4;
	const int PerSlot = 1024;

	/// <summary>
	/// A scope: a layer, setup, a list or the caller's own; its timestamps; its statistics query, -1 if none; what its list
	/// had recorded when it began, then what it recorded.
	/// </summary>
	public record struct Scope( string Name, ScopeKind Kind, int Begin, int End, int Query, Recorded Before, Recorded Recorded )
	{
		/// <summary>Whether it's a GPU counter sample too (<see cref="Device.Counters"/>).</summary>
		public bool Sampled { get; init; }
	}

	/// <summary>Draw calls, pipeline binds, descriptor set binds, rendering scopes and barrier calls recorded.</summary>
	public readonly record struct Recorded( int Draws, int Pipelines, int Sets, int Scopes, int Barriers )
	{
		public static Recorded Of( CommandList list )
		{
			var c = list.Counts;
			return new( c.Draws, c.Pipelines, c.Sets, c.Scopes, c.Barriers );
		}

		public Recorded Since( Recorded before ) => new( Draws - before.Draws, Pipelines - before.Pipelines, Sets - before.Sets, Scopes - before.Scopes, Barriers - before.Barriers );
	}

	/// <summary>What a scope times.</summary>
	public enum ScopeKind { Pass, List, Own }

	/// <summary>A scope's result: its name and kind, when it started and ended (ms from the frame's first timestamp), what the GPU counted and what was recorded.</summary>
	public readonly record struct Timing( string Name, ScopeKind Kind, double Start, double End, PipelineCounts Counts, Recorded Recorded )
	{
		public double Ms => End - Start;
	}

	/// <summary>A scope in the GPU's absolute timestamp domain, for joining an external trace.</summary>
	public readonly record struct TickTiming( string Name, ScopeKind Kind, ulong Start, ulong End, int Draws );

	readonly Device gpu;
	readonly GpuCounters counters;
	readonly TimestampQueries timestamps;
	readonly PipelineStatistics statistics;
	readonly List<Scope>[] scopes = new List<Scope>[Slots];
	readonly int[] usedTimestamps = new int[Slots];
	readonly int[] usedQueries = new int[Slots];
	readonly long[] frames = new long[Slots];
	readonly ulong[] deviceFrames = new ulong[Slots];
	int slot = -1;
	int resetPending;

	public PassTimer( Device gpu, bool statistics = false )
	{
		this.gpu = gpu;
		counters = gpu.Counters;
		timestamps = gpu.CreateTimestamps( Slots * PerSlot );
		if ( statistics && gpu.Capabilities.PipelineStatistics ) this.statistics = gpu.CreatePipelineStatistics( Slots * PerSlot / 2 );
		for ( int i = 0; i < Slots; i++ )
		{
			scopes[i] = new();
			frames[i] = -1;
		}
	}

	/// <summary>Whether scopes count pipeline statistics too.</summary>
	public bool Statistics => statistics is not null;

	/// <summary>
	/// Start timing frame <paramref name="frame"/>, in the slot of the frame <see cref="Slots"/> before, whose work the GPU
	/// has finished (<see cref="WaitForSlot"/>). Its queries are reset here on the CPU where the device can (lists then reach
	/// the GPU in any order), else in the first list that asks (<see cref="ResetIn"/>).
	/// </summary>
	public void BeginFrame( long frame )
	{
		counters?.BeginFrame();
		slot = (int)(frame % Slots);
		WaitForSlot( slot );
		lock ( scopes[slot] ) scopes[slot].Clear();
		usedTimestamps[slot] = 0;
		usedQueries[slot] = 0;
		frames[slot] = frame;
		deviceFrames[slot] = gpu.FrameNumber;
		if ( gpu.Capabilities.HostQueryReset )
		{
			timestamps.ResetOnHost( slot * PerSlot, PerSlot );
			statistics?.ResetOnHost( slot * PerSlot / 2, PerSlot / 2 );
			return;
		}

		Volatile.Write( ref resetPending, 1 );
	}

	/// <summary>Times <see cref="BeginFrame"/> waited for the GPU to finish a slot's last frame (<see cref="WaitForSlot"/>), for the tests.</summary>
	public int SlotWaits { get; private set; }

	/// <summary>
	/// Wait until the GPU has finished what timed the slot's last frame, before its queries are reset. The device has, when
	/// its own frames moved on <see cref="Slots"/> since (starting one waits for the frame <see cref="Device.FramesInFlight"/>
	/// before, and a list still recording when its frame closed goes with the next). The caller's frame numbers can move
	/// faster than the device's (the engine's frames, some of which draw nothing), leaving the slot's last frame one the GPU
	/// may still be writing: then everything submitted so far finishes first.
	/// </summary>
	void WaitForSlot( int s )
	{
		if ( frames[s] < 0 || deviceFrames[s] + 1 + (ulong)gpu.FramesInFlight <= gpu.FrameNumber ) return;

		SlotWaits++;
		gpu.Wait( gpu.LastSubmitted( QueueType.Graphics ) );
		gpu.Wait( gpu.LastSubmitted( QueueType.Compute ) );
	}

	/// <summary>
	/// Without resets on the CPU: reset the frame's queries in the first list that asks, before anything writes them.
	/// Outside rendering.
	/// </summary>
	internal void ResetIn( CommandList list )
	{
		if ( slot < 0 || Interlocked.Exchange( ref resetPending, 0 ) == 0 ) return;
		list.ResetTimestamps( timestamps, slot * PerSlot, PerSlot );
		if ( statistics is not null ) list.ResetQueries( statistics, slot * PerSlot / 2, PerSlot / 2 );
	}

	/// <summary>
	/// Start a scope in <paramref name="list"/>; -1 when the frame's run out. With <paramref name="count"/>, statistics
	/// too, which need the list outside rendering or in the scope it ends in, and no other counting scope open in it.
	/// The scope belongs to the frame it began in, even if the next frame begins before it ends.
	/// </summary>
	public int Begin( CommandList list, string name, ScopeKind kind, bool count = true )
	{
		if ( slot < 0 ) return -1;
		ref var used = ref usedTimestamps[slot];
		int begin;
		do
		{
			begin = Volatile.Read( ref used );
			if ( begin >= PerSlot - 1 ) return -1;
		}
		while ( Interlocked.CompareExchange( ref used, begin + 1, begin ) != begin );

		var query = -1;
		// Not on an async compute list: the pool counts graphics stages, which a compute-only queue can't begin (device lost)
		if ( statistics is not null && count && kind != ScopeKind.List && list.Queue == QueueType.Graphics )
		{
			query = Interlocked.Increment( ref usedQueries[slot] ) - 1;
			if ( query >= PerSlot / 2 ) query = -1;
		}

		list.WriteTimestamp( timestamps, slot * PerSlot + begin, PipelineStage.AllCommands );
		if ( query >= 0 ) list.BeginQuery( statistics, slot * PerSlot / 2 + query );

		// GPU counters sample passes and the caller's own scopes, not whole lists, outermost first
		var sampled = counters is not null && kind != ScopeKind.List && counters.BeginScope( list, name );

		lock ( scopes[slot] )
		{
			scopes[slot].Add( new Scope( name, kind, begin, -1, query, Recorded.Of( list ), default ) { Sampled = sampled } );
			return slot * PerSlot + scopes[slot].Count - 1;
		}
	}

	/// <summary>Whether a scope <see cref="Begin"/> started counts statistics.</summary>
	public bool Counts( int scope )
	{
		if ( scope < 0 ) return false;
		var (at, index) = (scope / PerSlot, scope % PerSlot);
		lock ( scopes[at] ) return index < scopes[at].Count && scopes[at][index].Query >= 0;
	}

	/// <summary>End a scope <see cref="Begin"/> started, in the same list.</summary>
	public void End( CommandList list, int scope )
	{
		if ( scope < 0 ) return;

		// In the slot of the frame it began in
		var (at, index) = (scope / PerSlot, scope % PerSlot);
		Scope s;
		lock ( scopes[at] )
		{
			if ( index >= scopes[at].Count ) return;
			s = scopes[at][index];
		}

		if ( s.Sampled ) counters.EndScope( list );

		// Out of timestamps: still end the query, which began
		var end = Interlocked.Increment( ref usedTimestamps[at] ) - 1;
		if ( end >= PerSlot )
		{
			if ( s.Query >= 0 ) list.EndQuery( statistics, at * PerSlot / 2 + s.Query );
			return;
		}

		if ( s.Query >= 0 ) list.EndQuery( statistics, at * PerSlot / 2 + s.Query );
		list.WriteTimestamp( timestamps, at * PerSlot + end, PipelineStage.AllCommands );
		lock ( scopes[at] ) scopes[at][index] = s with { End = end, Recorded = Recorded.Of( list ).Since( s.Before ) };
	}

	// What TryRead reads into, kept so reading a frame allocates nothing (the game reads one every frame)
	readonly ulong[] readTicks = new ulong[PerSlot];
	readonly List<Scope> readScopes = new( 64 );
	PipelineCounts[] readCounts = [];

	/// <summary>
	/// Read frame <paramref name="frame"/>'s scopes into <paramref name="into"/>, in the order they began on the GPU; false
	/// if the GPU hasn't finished them or the frame's slot has been reused. One caller at a time; allocates nothing once
	/// <paramref name="into"/> has grown.
	/// If supplied, <paramref name="absolute"/> also receives the scopes' raw ticks, in recording order.
	/// </summary>
	public bool TryRead( long frame, List<Timing> into, List<TickTiming> absolute = null )
	{
		into.Clear();
		absolute?.Clear();
		var s = (int)(frame % Slots);
		if ( frames[s] != frame ) return false;

		readScopes.Clear();
		lock ( scopes[s] ) readScopes.AddRange( scopes[s] );
		var used = Math.Min( usedTimestamps[s], PerSlot );
		if ( used == 0 ) return true;

		var ticks = readTicks.AsSpan( 0, used );
		if ( !timestamps.TryRead( s * PerSlot, ticks ) ) return false;

		var queries = Math.Min( usedQueries[s], PerSlot / 2 );
		if ( readCounts.Length < queries ) readCounts = new PipelineCounts[PerSlot / 2];
		var counts = readCounts.AsSpan( 0, queries );
		if ( statistics is not null && !statistics.TryRead( s * PerSlot / 2, counts ) ) return false;

		var origin = ulong.MaxValue;
		foreach ( var scope in readScopes )
		{
			if ( scope.End >= 0 && scope.Begin < used ) origin = Math.Min( origin, ticks[scope.Begin] );
		}

		var period = gpu.Capabilities.TimestampPeriod / 1e6;
		foreach ( var scope in readScopes )
		{
			if ( scope.End < 0 || scope.End >= used ) continue;
			var c = scope.Query >= 0 && scope.Query < counts.Length ? counts[scope.Query] : default;
			into.Add( new Timing( scope.Name, scope.Kind, (ticks[scope.Begin] - origin) * period, (ticks[scope.End] - origin) * period, c, scope.Recorded ) );
			absolute?.Add( new TickTiming( scope.Name, scope.Kind, ticks[scope.Begin], ticks[scope.End], scope.Recorded.Draws ) );
		}

		into.Sort( ( a, b ) => a.Start.CompareTo( b.Start ) );
		return true;
	}

	public void Dispose()
	{
		timestamps.Dispose();
		statistics?.Dispose();
	}
}
