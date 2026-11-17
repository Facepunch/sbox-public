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
	public record struct Scope( string Name, ScopeKind Kind, int Begin, int End, int Query, Recorded Before, Recorded Recorded );

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

	readonly Device gpu;
	readonly TimestampQueries timestamps;
	readonly PipelineStatistics statistics;
	readonly List<Scope>[] scopes = new List<Scope>[Slots];
	readonly int[] usedTimestamps = new int[Slots];
	readonly int[] usedQueries = new int[Slots];
	readonly long[] frames = new long[Slots];
	int slot = -1;
	int resetPending;

	public PassTimer( Device gpu, bool statistics = false )
	{
		this.gpu = gpu;
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

	/// <summary>Start timing frame <paramref name="frame"/>, in the slot of the frame <see cref="Slots"/> before.</summary>
	public void BeginFrame( long frame )
	{
		slot = (int)(frame % Slots);
		lock ( scopes[slot] ) scopes[slot].Clear();
		usedTimestamps[slot] = 0;
		usedQueries[slot] = 0;
		frames[slot] = frame;
		Volatile.Write( ref resetPending, 1 );
	}

	/// <summary>Reset the frame's queries in the first list that asks, before anything writes them. Outside rendering.</summary>
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
		var begin = Interlocked.Increment( ref usedTimestamps[slot] ) - 1;
		if ( begin >= PerSlot - 1 ) return -1;
		var query = -1;
		if ( statistics is not null && count && kind != ScopeKind.List )
		{
			query = Interlocked.Increment( ref usedQueries[slot] ) - 1;
			if ( query >= PerSlot / 2 ) query = -1;
		}

		list.WriteTimestamp( timestamps, slot * PerSlot + begin, PipelineStage.AllCommands );
		if ( query >= 0 ) list.BeginQuery( statistics, slot * PerSlot / 2 + query );

		lock ( scopes[slot] )
		{
			scopes[slot].Add( new Scope( name, kind, begin, -1, query, Recorded.Of( list ), default ) );
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

	/// <summary>
	/// Read frame <paramref name="frame"/>'s scopes into <paramref name="into"/>, in the order they began on the GPU; false
	/// if the GPU hasn't finished them or the frame's slot has been reused.
	/// </summary>
	public bool TryRead( long frame, List<Timing> into )
	{
		into.Clear();
		var s = (int)(frame % Slots);
		if ( frames[s] != frame ) return false;

		Scope[] list;
		lock ( scopes[s] ) list = [.. scopes[s]];
		var used = Math.Min( usedTimestamps[s], PerSlot );
		if ( used == 0 ) return true;

		var ticks = new ulong[used];
		if ( !timestamps.TryRead( s * PerSlot, ticks ) ) return false;

		var queries = Math.Min( usedQueries[s], PerSlot / 2 );
		var counts = new PipelineCounts[queries];
		if ( statistics is not null && !statistics.TryRead( s * PerSlot / 2, counts ) ) return false;

		var origin = ulong.MaxValue;
		foreach ( var scope in list )
		{
			if ( scope.End >= 0 && scope.Begin < used ) origin = Math.Min( origin, ticks[scope.Begin] );
		}

		var period = gpu.Capabilities.TimestampPeriod / 1e6;
		foreach ( var scope in list )
		{
			if ( scope.End < 0 || scope.End >= used ) continue;
			var c = scope.Query >= 0 && scope.Query < counts.Length ? counts[scope.Query] : default;
			into.Add( new Timing( scope.Name, scope.Kind, (ticks[scope.Begin] - origin) * period, (ticks[scope.End] - origin) * period, c, scope.Recorded ) );
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
