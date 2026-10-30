using NativeEngine;
using System.Text.Json;

namespace Sandbox.Diagnostics;

/// <summary>
/// One completed GPU frame, before scopes with the same name are aggregated. Offsets share a device clock.
/// </summary>
internal sealed record GpuFrameTimeline( string FrameId, int DroppedEvents, GpuFrameTimeline.Event[] Events )
{
	internal const int MaxEvents = 2048;
	internal const int MaxTelemetryBytes = 128 * 1024;

	public int Version => 4;
	public string Renderer { get; init; } = "unknown";
	public string Clock => "gpu-device";
	public string Boundaries => "elapsed";

	/// <summary>
	/// Only send fields used by the average timeline. Keep submission diagnostics in the local capture.
	/// Round milliseconds to nanoseconds to avoid serializing floating-point noise.
	/// </summary>
	internal object ToTelemetry()
	{
		var report = new
		{
			FrameId,
			DroppedEvents,
			Events = Events.Where( x => x.Kind == "pass" ).Select( x => new
			{
				x.Path,
				x.Queue,
				StartMs = Math.Round( x.StartMs, 6 ),
				EndMs = Math.Round( x.EndMs, 6 ),
				x.Kind
			} ).ToArray(),
			Version,
			Renderer,
			Clock,
			Boundaries
		};

		// Omit oversized frames as a whole so partial timelines cannot bias the average.
		return JsonSerializer.SerializeToUtf8Bytes( report ).Length <= MaxTelemetryBytes ? report : null;
	}

	public sealed record Event( string Name, int Parent, string Queue, double StartMs, double EndMs )
	{
		/// <summary>
		/// Escaped hierarchy with chronological sibling occurrences, for aggregation in ADX.
		/// </summary>
		public string Path { get; init; }
		public string Kind { get; init; }
		public string SubmissionId { get; init; }
		public string EndSubmissionId { get; init; }
		public string WaitForSubmissionId { get; init; }
		public string WaitStage { get; init; }
		public double DeviationMs { get; init; }
		public int CommandBuffers { get; init; }
		public bool WaitForAcquire { get; init; }

		/// <summary>
		/// A host submit's <see cref="System.Diagnostics.Stopwatch"/> time, sampled with the device clock at the submit,
		/// or 0 where it couldn't be.
		/// </summary>
		public long HostTicks { get; init; }

		/// <summary>
		/// A host submit's tag (<c>IRenderDevice::SetSubmitTag</c>): which caller's frame the submission was, or 0.
		/// </summary>
		public long SubmitTag { get; init; }
	}

	/// <summary>
	/// Read the already refreshed native snapshot. This never waits for the GPU.
	/// </summary>
	internal static GpuFrameTimeline Capture()
	{
		if ( !GpuProfilerStats.Enabled || GpuProfilerStats.FrameId == 0 )
		{
			return null;
		}

		int count = CSceneSystem.GetGpuTimestampEventCount();
		if ( count == 0 )
		{
			return null;
		}

		var events = new Event[Math.Min( count, MaxEvents )];
		for ( int i = 0; i < events.Length; i++ )
		{
			var name = CSceneSystem.GetGpuTimestampEvent( i, out var data );
			if ( name is null )
			{
				return null;
			}

			var start = data.StartMs;
			var end = data.EndMs;
			var queue = data.Queue;

			// Reject corrupt or discontinuous clocks instead of publishing a misleading trace.
			if ( !double.IsFinite( start ) || !double.IsFinite( end ) || start < 0 || end < start || end > 10_000 || queue is < 0 or > 1 )
			{
				return null;
			}

			if ( name.Length > 128 )
			{
				name = name[..128];
			}

			var parent = data.Parent;
			if ( parent < 0 || parent >= events.Length || parent == i )
			{
				parent = -1;
			}

			var kind = data.Kind;
			if ( kind is < 0 or > 3 )
			{
				return null;
			}

			var waitMask = data.WaitStageMask;
			events[i] = new( name, parent, queue == 1 ? "async-compute" : "graphics", start, end )
			{
				Kind = kind switch
				{
					1 => "submission",
					3 => "host-submit",
					_ => "pass"
				},
				SubmissionId = Id( data.SubmissionId ),
				EndSubmissionId = Id( data.EndSubmissionId ),
				WaitForSubmissionId = Id( data.WaitForSubmissionId ),
				WaitStage = waitMask == 0 ? null
					: waitMask == 0x800 ? "compute-shader"
					: waitMask == 0x10000 ? "all-commands"
					: $"0x{waitMask:X}",
				DeviationMs = data.DeviationMs,
				CommandBuffers = data.CommandBuffers,
				WaitForAcquire = data.WaitForAcquire,
				HostTicks = (long)data.HostTicks,
				SubmitTag = (long)data.SubmitTag
			};
		}

		if ( !BuildPaths( events ) )
		{
			return null;
		}

		return new( GpuProfilerStats.FrameId.ToString( System.Globalization.CultureInfo.InvariantCulture ), count - events.Length, events )
		{
			Renderer = GpuProfilerStats.Renderer
		};
	}

	internal static bool BuildPaths( Event[] events )
	{
		var indices = Enumerable.Range( 0, events.Length ).Where( i => events[i].Kind == "pass" ).ToArray();
		foreach ( var i in indices )
		{
			var e = events[i];
			if ( string.IsNullOrWhiteSpace( e.Name ) || e.Name.Length > 128 || e.Parent < -1 || e.Parent >= events.Length )
			{
				return false;
			}

			if ( e.Parent >= 0 )
			{
				var parent = events[e.Parent];
				if ( parent.Kind != "pass" || parent.Queue != e.Queue || parent.StartMs > e.StartMs || parent.EndMs < e.EndMs )
				{
					return false;
				}
			}
		}

		var children = indices.ToLookup( i => events[i].Parent );
		var pending = new Stack<(int Parent, string Path, int Depth)>();
		pending.Push( (-1, "", 0) );
		int visited = 0;
		while ( pending.TryPop( out var parent ) )
		{
			var occurrences = new Dictionary<(string Queue, string Name), int>();
			foreach ( var i in children[parent.Parent]
				.OrderBy( i => events[i].StartMs )
				.ThenBy( i => events[i].EndMs )
				.ThenBy( i => i ) )
			{
				var e = events[i];
				int occurrence = occurrences.GetValueOrDefault( (e.Queue, e.Name) ) + 1;
				occurrences[(e.Queue, e.Name)] = occurrence;
				var path = parent.Path + "/" + Uri.EscapeDataString( e.Name ) + "#" + occurrence.ToString( System.Globalization.CultureInfo.InvariantCulture );
				if ( parent.Depth > 32 || path.Length > 4096 )
				{
					return false;
				}

				events[i] = e with { Path = path };
				visited++;
				pending.Push( (i, path, parent.Depth + 1) );
			}
		}

		return visited == indices.Length; // A cycle has no reachable root.
	}

	/// <summary>
	/// The frame's events with each pass recorded in pieces back to back made one, from its first piece's start to its
	/// last's end, the pieces' children under it, and the paths rebuilt. The managed renderer records a layer in a piece
	/// per recording thread, each its own scope, and how many there are changes frame to frame - shown apart, a layer was
	/// a row of slivers that never held still. Pieces are consecutive siblings of one name on one queue.
	/// </summary>
	internal static Event[] MergePieces( Event[] events )
	{
		var into = new int[events.Length];
		var start = new double[events.Length];
		var end = new double[events.Length];
		for ( int i = 0; i < events.Length; i++ )
		{
			into[i] = i;
			start[i] = events[i].StartMs;
			end[i] = events[i].EndMs;
		}

		// Parents start no later than their children, so each is placed before what's under it
		var order = Enumerable.Range( 0, events.Length ).Where( i => events[i].Kind == "pass" )
			.OrderBy( i => events[i].StartMs ).ThenByDescending( i => events[i].EndMs ).ThenBy( i => i );

		var lastChild = new Dictionary<(int Parent, string Queue), int>();
		foreach ( var i in order )
		{
			var e = events[i];
			var parent = e.Parent >= 0 ? into[e.Parent] : -1;
			if ( lastChild.TryGetValue( (parent, e.Queue), out var previous ) && events[previous].Name == e.Name )
			{
				into[i] = previous;
				start[previous] = Math.Min( start[previous], e.StartMs );
				end[previous] = Math.Max( end[previous], e.EndMs );
				continue;
			}

			lastChild[(parent, e.Queue)] = i;
		}

		var index = new int[events.Length];
		var kept = new List<Event>( events.Length );
		for ( int i = 0; i < events.Length; i++ )
		{
			index[i] = -1;
			if ( into[i] != i ) continue;
			index[i] = kept.Count;
			kept.Add( events[i] );
		}

		var merged = kept.ToArray();
		for ( int i = 0; i < events.Length; i++ )
		{
			if ( index[i] < 0 ) continue;
			var e = events[i];
			var parent = e.Parent >= 0 ? index[into[e.Parent]] : -1;
			merged[index[i]] = e with { Parent = parent, StartMs = start[i], EndMs = end[i] };
		}

		return BuildPaths( merged ) ? merged : events;
	}

	static string Id( ulong value ) => value == 0 ? null : value.ToString( System.Globalization.CultureInfo.InvariantCulture );
}
