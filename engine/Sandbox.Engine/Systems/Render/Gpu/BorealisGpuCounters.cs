using Borealis.Gpu;
using System.Globalization;
using System.Text;
using System.Threading.Tasks;

namespace Sandbox;

/// <summary>
/// Hardware performance counters per GPU pass in the game window, where the game was started with <c>-gpucounters
/// [selection]</c> (<see cref="DeviceOptions.GpuCounters"/>): <c>r_gpucounters [samples]</c> samples the frames that
/// follow (hold the view still) and writes <c>logs/gpucounters.csv</c> and <c>logs/gpucounters.txt</c>, the table also to
/// the log. See docs/reference/gpu-counters.md.
/// </summary>
internal static class BorealisGpuCounters
{
	static Task running;

	[ConCmd( "r_gpucounters", Help = "Sample the GPU's hardware counters per pass over the next frames (needs -gpucounters); writes logs/gpucounters.csv and .txt" )]
	internal static void Capture( int samples = 4 )
	{
		if ( RenderBackend.Default is not BorealisGpuRenderBackend backend )
		{
			Log.Warning( "r_gpucounters: the game isn't drawing with Borealis.Gpu" );
			return;
		}

		if ( backend.Device.Counters is not { } counters )
		{
			Log.Warning( "r_gpucounters: GPU counters are off; start the game with -gpucounters [preset or names] on an AMD GPU (see docs/reference/gpu-counters.md)" );
			return;
		}

		if ( running is { IsCompleted: false } )
		{
			Log.Warning( "r_gpucounters: a capture is already running" );
			return;
		}

		var folder = EngineFileSystem.Root.GetFullPath( "/logs" );
		if ( counters.ListRequested ) WriteList( counters, System.IO.Path.Combine( folder, "gpucounters-list.csv" ) );

		backend.StartCounterTimeline();
		counters.Capture( samples );
		Log.Info( $"r_gpucounters: sampling {counters.Counters.Count} counters, {samples} samples of {counters.PassCount} frames each" );
		running = Task.Run( async () =>
		{
			var waited = 0;
			while ( counters.Capturing && waited++ < 1200 ) await Task.Delay( 100 );
			var timeline = backend.StopCounterTimeline();
			var results = counters.Results;
			var report = Report( counters, results, timeline );
			System.IO.File.WriteAllText( System.IO.Path.Combine( folder, "gpucounters.csv" ), Csv( results, timeline ) );
			System.IO.File.WriteAllText( System.IO.Path.Combine( folder, "gpucounters.txt" ), report );
			Log.Info( $"r_gpucounters: written to {folder}\\gpucounters.csv\n{report}" );
		} );
	}

	static string Report( GpuCounters counters, GpuCounterResults results, Dictionary<string, double> timeline )
	{
		var text = new StringBuilder();
		var (done, failed) = counters.Sessions;
		text.AppendLine( FormattableString.Invariant( $"GPU counters over {done} samples of {counters.PassCount} frames each ({failed} given up), {counters.Hardware.Name} {counters.Hardware.Generation}" ) );
		if ( counters.Missing.Count > 0 ) text.AppendLine( $"Not on this GPU: {string.Join( ", ", counters.Missing )}" );
		text.AppendLine();
		text.Append( results.ToTable() );

		var time = results.IndexOf( "GPUTime" );
		if ( time < 0 ) return text.ToString();
		text.AppendLine( "scope                                     GPUTime ms  timeline ms" );
		foreach ( var s in results.Scopes.Take( 25 ) )
		{
			var gpa = results.Counters[time].Unit == GpuCounterUnit.Nanoseconds ? s.Values[time] / 1e6 : s.Values[time];
			var name = s.Name.Length > 40 ? s.Name[..39] + "~" : s.Name;
			text.AppendLine( FormattableString.Invariant( $"{name,-40}  {gpa,10:0.000}  {(timeline.TryGetValue( s.Name, out var ms ) ? ms.ToString( "0.000", CultureInfo.InvariantCulture ) : "-"),11}" ) );
		}

		return text.ToString();
	}

	static string Csv( GpuCounterResults results, Dictionary<string, double> timeline )
	{
		var rows = results.ToCsv().Split( '\n', StringSplitOptions.RemoveEmptyEntries ).Select( l => l.TrimEnd( '\r' ) ).ToList();
		var csv = new StringBuilder( rows[0] ).AppendLine( ",timeline_ms" );
		for ( int i = 1; i < rows.Count; i++ )
		{
			var name = results.Scopes[i - 1].Name;
			csv.Append( rows[i] ).Append( ',' ).AppendLine( timeline.TryGetValue( name, out var ms ) ? ms.ToString( "0.0000", CultureInfo.InvariantCulture ) : "" );
		}

		return csv.ToString();
	}

	static void WriteList( GpuCounters counters, string path )
	{
		static string Quote( string text ) => '"' + text.Replace( "\"", "\"\"" ) + '"';
		var csv = new StringBuilder( "index,name,group,unit,discrete,description\n" );
		foreach ( var c in counters.Available )
			csv.AppendLine( string.Join( ',', c.Index.ToString( CultureInfo.InvariantCulture ), Quote( c.Name ), Quote( c.Group ), c.Unit, c.Discrete, Quote( c.Description ) ) );
		System.IO.File.WriteAllText( path, csv.ToString() );
		Log.Info( $"r_gpucounters: {counters.Available.Count} counters on the device, listed in {path}" );
	}
}

internal sealed partial class BorealisGpuRenderBackend
{
	// The timeline's ms per pass name while r_gpucounters samples, from the busy timer's reads: null when not sampling
	Dictionary<string, double> counterTimeline;
	int counterTimelineFrames;
	readonly object counterGate = new();

	internal void StartCounterTimeline()
	{
		lock ( counterGate ) { counterTimeline = new(); counterTimelineFrames = 0; }
	}

	internal Dictionary<string, double> StopCounterTimeline()
	{
		lock ( counterGate )
		{
			var ms = counterTimeline ?? new();
			counterTimeline = null;
			foreach ( var name in ms.Keys.ToList() ) ms[name] /= Math.Max( 1, counterTimelineFrames );
			return ms;
		}
	}

	void AddCounterTimeline( List<Borealis.Backend.PassTimer.Timing> timings )
	{
		lock ( counterGate )
		{
			if ( counterTimeline is null ) return;
			counterTimelineFrames++;
			foreach ( var t in timings )
			{
				if ( t.Kind != Borealis.Backend.PassTimer.ScopeKind.List ) counterTimeline[t.Name] = counterTimeline.GetValueOrDefault( t.Name ) + t.Ms;
			}
		}
	}
}
