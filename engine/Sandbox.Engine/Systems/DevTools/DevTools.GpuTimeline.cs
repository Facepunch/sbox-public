namespace Sandbox.Diagnostics;

internal static partial class DevTools
{
	/// <summary>
	/// One GPU pass where it ran in the frame, in ms from the frame's first submit or pass. Averaged over the last few
	/// captures, so the layout holds still.
	/// </summary>
	internal readonly record struct GpuSpan( string Path, string Name, int Depth, bool Async, bool Leaf, float StartMs, float EndMs, int Family )
	{
		public float Ms => EndMs - StartMs;
	}

	/// <summary>
	/// The GPU's frame as it ran: every pass at its offset, the moments the CPU submitted work, and how much of the
	/// elapsed time the GPU was actually busy. Gaps between passes are the GPU waiting.
	/// </summary>
	internal sealed class GpuLayout
	{
		public readonly List<GpuSpan> Spans = new();
		public readonly List<float> Submits = new();

		/// <summary>
		/// From the first submit or pass to the last pass ending.
		/// </summary>
		public float ElapsedMs;

		/// <summary>
		/// Time the graphics queue had a pass running, and the async compute queue.
		/// </summary>
		public float BusyMs, AsyncBusyMs;

		public bool HasAsync;
		public int MaxDepth, MaxAsyncDepth;
	}

	/// <summary>
	/// The latest GPU frame layout, rebuilt with the analysis. Empty until a timeline arrives.
	/// </summary>
	internal static GpuLayout GpuTimeline { get; private set; } = new();

	/// <summary>
	/// Whether the GPU's frame is open at its own scale under the budget ruler.
	/// </summary>
	internal static bool GpuZoom { get; set; }

	sealed class SmoothedSpan
	{
		public string Name;
		public int Depth, Family;
		public bool Async, Leaf;
		public float Start, End;
		public double SeenAt;
	}

	static readonly Dictionary<string, SmoothedSpan> _gpuSpans = new();
	static readonly List<float> _gpuSubmits = new();

	/// <summary>
	/// Fold the latest captured GPU frame into the averaged layout.
	/// </summary>
	static void UpdateGpuTimeline()
	{
		var frame = GpuFrameTimeline.Capture();
		var now = RealTime.Now;

		if ( frame is not null )
		{
			var events = frame.Events;
			var hasChild = new bool[events.Length];
			for ( int i = 0; i < events.Length; i++ )
				if ( events[i].Kind == "pass" && events[i].Parent >= 0 ) hasChild[events[i].Parent] = true;

			var submit = 0;
			for ( int i = 0; i < events.Length; i++ )
			{
				var e = events[i];
				if ( e.Kind == "host-submit" )
				{
					// The nth submit of the frame, averaged like the passes
					if ( submit < _gpuSubmits.Count ) _gpuSubmits[submit] = _gpuSubmits[submit].LerpTo( (float)e.StartMs, 0.3f );
					else _gpuSubmits.Add( (float)e.StartMs );
					submit++;
					continue;
				}

				if ( e.Kind != "pass" || e.Path is null ) continue;

				// Depth, and the top level pass it's under - its family, for colour
				var depth = 0;
				var top = i;
				for ( int p = e.Parent; p >= 0 && depth < 32; p = events[p].Parent ) { depth++; top = p; }

				if ( !_gpuSpans.TryGetValue( e.Path, out var span ) )
				{
					span = new SmoothedSpan { Start = (float)e.StartMs, End = (float)e.EndMs };
					_gpuSpans[e.Path] = span;
				}

				span.Name = e.Name;
				span.Depth = depth;
				span.Async = e.Queue == "async-compute";
				span.Leaf = !hasChild[i];
				span.Family = (events[top].Name.GetHashCode() & 0x7fffffff) % DevToolsStyles.Series.Length;
				span.Start = span.Start.LerpTo( (float)e.StartMs, 0.3f );
				span.End = span.End.LerpTo( (float)e.EndMs, 0.3f );
				span.SeenAt = now;
			}

			if ( submit < _gpuSubmits.Count ) _gpuSubmits.RemoveRange( submit, _gpuSubmits.Count - submit );
		}

		// Passes that stopped running drop out after a couple of seconds
		foreach ( var key in _gpuSpans.Where( x => now - x.Value.SeenAt > 2 ).Select( x => x.Key ).ToList() )
			_gpuSpans.Remove( key );

		var layout = new GpuLayout();
		foreach ( var (path, s) in _gpuSpans )
		{
			layout.Spans.Add( new GpuSpan( path, s.Name, s.Depth, s.Async, s.Leaf, s.Start, MathF.Max( s.Start, s.End ), s.Family ) );
			layout.ElapsedMs = MathF.Max( layout.ElapsedMs, s.End );
			if ( s.Async ) { layout.HasAsync = true; layout.MaxAsyncDepth = Math.Max( layout.MaxAsyncDepth, s.Depth ); }
			else layout.MaxDepth = Math.Max( layout.MaxDepth, s.Depth );
		}

		layout.Spans.Sort( static ( a, b ) => a.StartMs.CompareTo( b.StartMs ) );
		layout.Submits.AddRange( _gpuSubmits );
		layout.BusyMs = Busy( layout.Spans, async: false );
		layout.AsyncBusyMs = Busy( layout.Spans, async: true );
		GpuTimeline = layout;
	}

	/// <summary>
	/// How long a queue had any leaf pass running - the union of their spans, so overlaps count once.
	/// </summary>
	static float Busy( List<GpuSpan> spans, bool async )
	{
		float busy = 0, end = float.MinValue;
		foreach ( var s in spans )
		{
			if ( s.Async != async || !s.Leaf ) continue;
			if ( s.StartMs >= end ) { busy += s.Ms; end = s.EndMs; }
			else if ( s.EndMs > end ) { busy += s.EndMs - end; end = s.EndMs; }
		}

		return busy;
	}
}
