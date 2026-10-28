namespace Sandbox.Diagnostics;

internal static partial class DevTools
{
	/// <summary>
	/// One GPU pass where it ran in the frame, in ms from the frame's first submit or pass, averaged over the frames
	/// sampled in the analysis window so the layout holds still. <see cref="AvgMs"/> and <see cref="MaxMs"/> are its
	/// duration over those frames. <see cref="Parent"/> is the path of the pass it's nested in, or null at the top.
	/// </summary>
	internal readonly record struct GpuSpan( string Path, string Parent, string Name, int Depth, bool Async, bool Leaf, float StartMs, float EndMs, PassKind Kind )
	{
		public float Ms => EndMs - StartMs;
		public float AvgMs { get; init; }
		public float MaxMs { get; init; }
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
		/// From the first submit or pass to the last pass ending - with the wait before the GPU starts on the frame.
		/// </summary>
		public float ElapsedMs;

		/// <summary>
		/// From the GPU's first pass starting to its last ending: the stretch it worked on this frame.
		/// </summary>
		public float ActiveMs;

		/// <summary>
		/// How much of <see cref="ActiveMs"/> the graphics queue had a pass running.
		/// </summary>
		public float BusyShare => ActiveMs > 0 ? MathF.Min( 1, BusyMs / ActiveMs ) : 0;

		/// <summary>
		/// Time the graphics queue had a pass running, and the async compute queue.
		/// </summary>
		public float BusyMs, AsyncBusyMs;

		public bool HasAsync;
		public int MaxDepth, MaxAsyncDepth;

		/// <summary>
		/// The managed renderer's frame plan when the layout was built, so its CPU side holds still with the GPU's - and with
		/// everything else while the dev tools is paused. Null through native.
		/// </summary>
		public Sandbox.Rendering.ManagedFramePlan Plan;

		/// <summary>
		/// For one captured frame rather than the averages: the frame it was captured on (<see cref="FrameHistory.Number"/>)
		/// and the frame picked in a chart it stands in for. Zero for the averages.
		/// </summary>
		public long CapturedOn, Picked;
	}

	/// <summary>
	/// One captured GPU frame as it was, kept so a frame picked in a chart can show a real frame rather than the averages.
	/// <see cref="Frame"/> is the frame it was captured on; GPU results arrive a few frames late, so the GPU work in it is
	/// a little older.
	/// </summary>
	sealed record GpuCapture( long Frame, List<GpuSpan> Spans, List<float> Submits, Sandbox.Rendering.ManagedFramePlan Plan );

	/// <summary>
	/// The captures of the last ten seconds or so, oldest first.
	/// </summary>
	static readonly List<GpuCapture> _captures = new();
	const int CaptureHistory = 100;

	static GpuLayout _pickedLayout;
	static long _pickedFor;

	/// <summary>
	/// The GPU frame to show: the averaged layout, or, with a frame picked in a chart, the capture taken soonest after it -
	/// GPU results arrive late, so a frame's own GPU work is in a later capture.
	/// </summary>
	internal static GpuLayout ShownGpuTimeline
	{
		get
		{
			if ( SelectedFrame is not { } picked || _captures.Count == 0 ) return GpuTimeline;
			if ( _pickedLayout is not null && _pickedFor == picked ) return _pickedLayout;

			var capture = _captures.FirstOrDefault( x => x.Frame >= picked ) ?? _captures[^1];
			_pickedLayout = LayoutOf( capture.Spans, capture.Submits, capture.Plan );
			_pickedLayout.CapturedOn = capture.Frame;
			_pickedLayout.Picked = picked;
			_pickedFor = picked;
			return _pickedLayout;
		}
	}

	/// <summary>
	/// The latest GPU frame layout, rebuilt with the analysis. Empty until a timeline arrives.
	/// </summary>
	internal static GpuLayout GpuTimeline { get; private set; } = new();

	/// <summary>
	/// How many samples of each pass are kept: its history, for the Rendering tab's charts.
	/// </summary>
	internal const int GpuSpanHistory = 100;

	/// <summary>
	/// A captured frame's pass: where it started, how long it took, and when it was captured.
	/// </summary>
	readonly record struct SpanSample( float Start, float Ms, double At );

	sealed class SampledSpan
	{
		public string Name, Parent;
		public int Depth;
		public PassKind Kind;
		public bool Async, Leaf;
		public double SeenAt;
		public readonly SpanSample[] Samples = new SpanSample[GpuSpanHistory];
		public int Head, Count;

		public void Add( SpanSample sample )
		{
			Samples[Head] = sample;
			Head = (Head + 1) % Samples.Length;
			Count = Math.Min( Count + 1, Samples.Length );
		}

		public SpanSample At( int age ) => Samples[(Head - 1 - age + Samples.Length * 2) % Samples.Length];
	}

	static readonly Dictionary<string, SampledSpan> _gpuSpans = new();
	static readonly List<float> _gpuSubmits = new();
	static RealTimeSince _sinceGpuSample = 1;

	/// <summary>
	/// How often a GPU frame is captured. Each pass's average and max are over the frames captured in the analysis window,
	/// so the Rendering tab, which lists them, captures five times as often as the overview's ruler needs.
	/// </summary>
	static float GpuSampleInterval => IsCollecting( "rendering" ) ? 0.1f : AnalysisInterval;

	/// <summary>
	/// A pass's duration in the frames captured <paramref name="age"/> samples ago, newest first, for a chart of it.
	/// </summary>
	internal static float GpuSpanSample( string path, int age ) =>
		_gpuSpans.TryGetValue( path, out var span ) && age < span.Count ? span.At( age ).Ms : 0;

	internal static int GpuSpanSamples( string path ) => _gpuSpans.TryGetValue( path, out var span ) ? span.Count : 0;

	/// <summary>
	/// Capture the latest completed GPU frame into each pass's samples, when one is due.
	/// </summary>
	static void SampleGpuTimeline( bool force )
	{
		if ( !force && _sinceGpuSample < GpuSampleInterval ) return;
		_sinceGpuSample = 0;

		var frame = GpuFrameTimeline.Capture();
		if ( frame is null ) return;

		var now = RealTime.Now;
		var events = frame.Events;

		var submit = 0;
		foreach ( var e in events )
		{
			if ( e.Kind != "host-submit" ) continue;

			// The nth submit of the frame, eased like the passes
			if ( submit < _gpuSubmits.Count ) _gpuSubmits[submit] = _gpuSubmits[submit].LerpTo( (float)e.StartMs, 0.2f );
			else _gpuSubmits.Add( (float)e.StartMs );
			submit++;
		}

		if ( submit < _gpuSubmits.Count ) _gpuSubmits.RemoveRange( submit, _gpuSubmits.Count - submit );

		// A managed layer recorded in pieces is one pass (GpuFrameTimeline.MergePieces). Native's passes stay apart: the
		// same pass in each shadow cascade is its own
		var passes = FrameStats.Current.Managed.Renders > 0 ? GpuFrameTimeline.MergePieces( events ) : events;
		var hasChild = new bool[passes.Length];
		for ( int i = 0; i < passes.Length; i++ )
			if ( passes[i].Kind == "pass" && passes[i].Parent >= 0 ) hasChild[passes[i].Parent] = true;

		// What kind of work each pass is, for colour: its own name's, else what it's in. Parents come first
		var kinds = new PassKind[passes.Length];
		foreach ( var i in Enumerable.Range( 0, passes.Length ).Where( i => passes[i].Kind == "pass" ).OrderBy( i => passes[i].StartMs ).ThenByDescending( i => passes[i].EndMs ) )
			kinds[i] = KindOf( passes[i].Name, passes[i].Parent >= 0 ? kinds[passes[i].Parent] : PassKind.Other );

		var captured = new List<GpuSpan>();
		for ( int i = 0; i < passes.Length; i++ )
		{
			var e = passes[i];
			if ( e.Kind != "pass" || e.Path is null ) continue;

			var depth = 0;
			for ( int p = e.Parent; p >= 0 && depth < 32; p = passes[p].Parent ) depth++;

			var parent = e.Parent >= 0 ? passes[e.Parent].Path : null;
			var ms = (float)(e.EndMs - e.StartMs);
			captured.Add( new GpuSpan( e.Path, parent, e.Name, depth, e.Queue == "async-compute", !hasChild[i], (float)e.StartMs, (float)e.EndMs, kinds[i] ) { AvgMs = ms } );

			if ( !_gpuSpans.TryGetValue( e.Path, out var span ) )
			{
				span = new SampledSpan();
				_gpuSpans[e.Path] = span;
			}

			span.Name = e.Name;
			span.Parent = parent;
			span.Depth = depth;
			span.Async = e.Queue == "async-compute";
			span.Leaf = !hasChild[i];
			span.Kind = kinds[i];
			span.SeenAt = now;
			span.Add( new SpanSample( (float)e.StartMs, (float)(e.EndMs - e.StartMs), now ) );
		}

		// Passes that stopped running drop out after a couple of seconds
		foreach ( var key in _gpuSpans.Where( x => now - x.Value.SeenAt > 2 ).Select( x => x.Key ).ToList() )
			_gpuSpans.Remove( key );

		// The frame as it was, for picking frames later
		var submits = events.Where( x => x.Kind == "host-submit" ).Select( x => (float)x.StartMs ).ToList();
		var plan = FrameStats.Current.Managed.Renders > 0 && Sandbox.Rendering.ManagedFramePlan.Latest is { SubmitTicks: > 0 } latest ? latest : null;
		_captures.Add( new GpuCapture( Frames.Number( 0 ), captured, submits, plan ) );
		if ( _captures.Count > CaptureHistory ) _captures.RemoveAt( 0 );
		_pickedLayout = null;
	}

	/// <summary>
	/// Lay out the GPU frame from the samples in the analysis window: each pass at its average start, its average length.
	/// </summary>
	static GpuLayout BuildGpuLayout()
	{
		var now = RealTime.Now;
		var spans = new List<GpuSpan>();
		foreach ( var (path, s) in _gpuSpans )
		{
			float start = 0, sum = 0, max = 0;
			var n = 0;
			for ( int age = 0; age < s.Count; age++ )
			{
				var sample = s.At( age );
				if ( n > 0 && now - sample.At > AnalysisWindowMs / 1000f ) break;
				start += sample.Start;
				sum += sample.Ms;
				max = MathF.Max( max, sample.Ms );
				n++;
			}

			if ( n == 0 ) continue;
			start /= n;
			var avg = sum / n;

			spans.Add( new GpuSpan( path, s.Parent, s.Name, s.Depth, s.Async, s.Leaf, start, start + avg, s.Kind ) { AvgMs = avg, MaxMs = max } );
		}

		var plan = FrameStats.Current.Managed.Renders > 0 && Sandbox.Rendering.ManagedFramePlan.Latest is { SubmitTicks: > 0 } latest ? latest : null;
		return LayoutOf( spans, _gpuSubmits, plan );
	}

	/// <summary>
	/// A GPU frame's layout from its passes: where it starts and ends, how busy each queue was, how deep they nest.
	/// </summary>
	static GpuLayout LayoutOf( List<GpuSpan> spans, List<float> submits, Sandbox.Rendering.ManagedFramePlan plan )
	{
		var layout = new GpuLayout();
		foreach ( var s in spans )
		{
			layout.Spans.Add( s );
			layout.ElapsedMs = MathF.Max( layout.ElapsedMs, s.EndMs );
			if ( s.Async ) { layout.HasAsync = true; layout.MaxAsyncDepth = Math.Max( layout.MaxAsyncDepth, s.Depth ); }
			else layout.MaxDepth = Math.Max( layout.MaxDepth, s.Depth );
		}

		layout.Spans.Sort( static ( a, b ) => a.StartMs.CompareTo( b.StartMs ) );
		layout.Submits.AddRange( submits );
		layout.Plan = plan;
		if ( layout.Spans.Count > 0 ) layout.ActiveMs = layout.ElapsedMs - layout.Spans[0].StartMs;
		layout.BusyMs = Busy( layout.Spans, async: false );
		layout.AsyncBusyMs = Busy( layout.Spans, async: true );
		return layout;
	}

	/// <summary>
	/// What a pass does, by its name, for its colour on the timeline and in the tree - the way Unity colours its markers
	/// by category. A name that says nothing - a batch, a command list - is the kind of the pass it's in.
	/// </summary>
	internal static PassKind KindOf( string name, PassKind parent )
	{
		// The same names come in every capture, ten a second: worked out once each
		if ( _kinds.TryGetValue( (name, parent), out var known ) ) return known;

		var kind = Classify( name.ToLowerInvariant() ) ?? parent;
		if ( _kinds.Count < 4096 ) _kinds[(name, parent)] = kind;
		return kind;
	}

	static readonly Dictionary<(string Name, PassKind Parent), PassKind> _kinds = new();

	static readonly string[] StageUi = ["ui", "overlay", "hud"];
	static readonly string[] PostWords = ["ambient occlusion", "ao ", "ssr", "reflection", "bloom", "tonemap", "vignette", "exposure", "post", "downsample", "upsample", "denoise", "blur", "fog", "contact", "resolve", "output"];
	static readonly string[] WorldOverlayWords = ["world overlays"];
	static readonly string[] UiWords = ["ui root", "ui overlay", "hud", "panel", "engine overlay", "platformoverlay"];
	static readonly string[] TranslucentWords = ["translucent", "refraction", "sprite", "particle", "decal", "gameoverlay", "viewmodel"];
	static readonly string[] OtherWords = ["setup", "light", "binner", "cluster", "clear", "async compute", "copy"];
	static readonly string[] DepthWords = ["depth", "shadow", "csm", "occlusion", "hi-z", "prepass"];
	static readonly string[] OpaqueWords = ["opaque", "forward", "sky"];

	/// <summary>
	/// A lowercase name's kind, or null when the name says nothing.
	/// </summary>
	static PassKind? Classify( string n )
	{
		static bool Any( string n, string[] words )
		{
			foreach ( var word in words )
				if ( n.Contains( word, StringComparison.Ordinal ) ) return true;
			return false;
		}

		// The managed renderer's stage hooks hold post-processing and command lists, or the UI
		if ( n.StartsWith( "managed:", StringComparison.Ordinal ) ) return Any( n, StageUi ) ? PassKind.Ui : PassKind.Post;
		if ( Any( n, PostWords ) ) return PassKind.Post;
		if ( Any( n, WorldOverlayWords ) ) return PassKind.Opaque;
		if ( Any( n, UiWords ) ) return PassKind.Ui;
		if ( Any( n, TranslucentWords ) ) return PassKind.Translucent;
		if ( Any( n, OtherWords ) ) return PassKind.Other;
		if ( Any( n, DepthWords ) ) return PassKind.Depth;
		if ( Any( n, OpaqueWords ) ) return PassKind.Opaque;
		return null;
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
