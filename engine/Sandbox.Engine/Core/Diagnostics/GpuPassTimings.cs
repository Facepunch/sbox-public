namespace Sandbox.Diagnostics;

/// <summary>
/// Raw, inclusive GPU scope times, aggregated per completed frame rather than per readback.
/// Does not use the overlay's smoothed values. Owned by Api.Performance under its lock.
/// </summary>
internal sealed class GpuPassTimings
{
	internal const int MaxPasses = 256;
	internal const int MaxPathLength = 512;

	internal sealed record Metric( int Frames, double SumMs, double MaxMs );
	internal sealed record Report( int SampledFrames, int DroppedPassSamples, Dictionary<string, Metric> Passes )
	{
		public int Version => 2;
		public string Renderer { get; init; } = "unknown";
		public string Queue => "all";
		public string Boundaries => "elapsed";
		public bool Inclusive => true;
	}

	struct Accumulator
	{
		public int Frames;
		public double Sum;
		public double Max;

		public void Add( double ms )
		{
			Max = Math.Max( Max, ms );
			Sum += ms;
			Frames++;
		}
	}

	readonly Dictionary<string, Accumulator> passes = new( StringComparer.Ordinal );
	readonly Dictionary<string, double> framePasses = new( StringComparer.Ordinal );
	ulong lastFrame;
	int sampledFrames;
	int droppedPassSamples;
	string renderer = "unknown";

	// On enabling timestamps, the native snapshot may still contain the previous sampling window.
	internal void SkipFrame( ulong frameId ) => lastFrame = frameId;

	internal void AddFrame( ulong frameId, ReadOnlySpan<GpuProfilerStats.Row> rows, string frameRenderer = "unknown" )
	{
		if ( frameId == 0 || frameId == lastFrame || rows.IsEmpty )
		{
			return;
		}

		lastFrame = frameId;

		// A heartbeat contains one cohort. Discard the previous partial window on a mode switch.
		if ( renderer != frameRenderer )
		{
			Clear();
		}

		renderer = frameRenderer;
		sampledFrames++;
		framePasses.Clear();
		var paths = new string[rows.Length];

		for ( int i = 0; i < rows.Length; i++ )
		{
			ref readonly var row = ref rows[i];

			// Native emits parents first. Reject malformed/oversized paths without following cycles.
			if ( string.IsNullOrEmpty( row.Name ) || row.Name.Length > MaxPathLength || row.Parent < -1 || row.Parent >= i
				|| (row.Parent >= 0 && paths[row.Parent] is null) )
			{
				if ( row.Measured )
				{
					droppedPassSamples++;
				}

				continue;
			}

			// Escape separators so a marker named "A/B" cannot collide with nested A -> B.
			var name = row.Name.Replace( "%", "%25" ).Replace( "/", "%2F" );
			var path = row.Parent < 0 ? name : $"{paths[row.Parent]}/{name}";
			if ( path.Length > MaxPathLength )
			{
				if ( row.Measured )
				{
					droppedPassSamples++;
				}

				continue;
			}

			paths[i] = path;

			// View/group rows have no timestamp of their own.
			if ( !row.Measured )
			{
				continue;
			}

			if ( !float.IsFinite( row.Duration ) || row.Duration < 0 )
			{
				droppedPassSamples++;
				continue;
			}

			if ( !passes.ContainsKey( path ) && !framePasses.ContainsKey( path ) && passes.Count >= MaxPasses )
			{
				droppedPassSamples++;
				continue;
			}

			passes.TryAdd( path, default );
			framePasses.TryGetValue( path, out var duration );
			framePasses[path] = duration + row.Duration;
		}

		foreach ( var (path, duration) in framePasses )
		{
			var pass = passes[path];
			pass.Add( duration );
			passes[path] = pass;
		}
	}

	internal Report Flip()
	{
		var report = sampledFrames == 0 ? null : new Report( sampledFrames, droppedPassSamples,
			passes.ToDictionary( x => x.Key, x => new Metric( x.Value.Frames,
				Math.Round( x.Value.Sum, 6 ), Math.Round( x.Value.Max, 6 ) ) ) )
		{
			Renderer = renderer
		};

		Clear();
		return report;
	}

	internal void Clear()
	{
		passes.Clear();
		framePasses.Clear();
		sampledFrames = droppedPassSamples = 0;
		// Retain lastFrame across heartbeat boundaries: a stalled GPU is not a fresh sample.
	}
}

/// <summary>
/// Limit timestamp instrumentation to one second out of every thirty during eligible play.
/// </summary>
internal sealed class GpuTimingSamplingWindow
{
	internal const double Duration = 1;
	internal const double Interval = 30;

	double nextWindow;
	double windowEnd;

	internal bool Update( double now, bool allowed )
	{
		if ( !allowed )
		{
			Reset();
			return false;
		}

		if ( now >= nextWindow )
		{
			windowEnd = now + Duration;
			nextWindow = now + Interval;
		}

		return now < windowEnd;
	}

	internal void Reset() => nextWindow = windowEnd = 0;
}
