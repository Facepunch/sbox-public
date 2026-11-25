using Borealis.Gpu;
using System.Globalization;
using System.IO;
using System.Text;

namespace Sandbox.BorealisLab.Maps;

/// <summary>
/// <c>-gpucounters [selection]</c>: after a view's timed frames, sample the device's hardware counters
/// (<see cref="Device.Counters"/>) over the same still view and report them per GPU scope, beside the timeline's times.
/// <c>-gpucounters-samples N</c> samples (4 by default), each <see cref="GpuCounters.PassCount"/> frames.
/// </summary>
internal static class GpuCounterRun
{
	public static string Run( GpuCounters counters, PassTotals passes, string title, Action renderFrame, StringBuilder csv )
	{
		var timed = passes.MsPerName();
		passes.Clear();

		var samples = Math.Max( 1, Args.Int( "-gpucounters-samples", 4 ) );
		counters.Capture( samples );
		var frames = 0;
		var limit = samples * counters.PassCount * 4 + 600;
		while ( counters.Capturing && frames < limit )
		{
			renderFrame();
			frames++;
		}

		// The pass timer reads frames a few behind
		for ( int i = 0; i < 4; i++ ) renderFrame();

		var results = counters.Results;
		var during = passes.MsPerName();
		var text = new StringBuilder();
		var (done, failed) = counters.Sessions;
		text.AppendLine( FormattableString.Invariant( $"{title}: GPU counters over {done} samples of {counters.PassCount} frames each ({frames} frames, {failed} samples given up), {counters.Hardware.Name} {counters.Hardware.Generation}" ) );
		if ( counters.Missing.Count > 0 ) text.AppendLine( $"Not on this GPU: {string.Join( ", ", counters.Missing )}" );
		text.AppendLine();
		text.Append( results.ToTable() );

		// GPA's GPU time against the timestamps' over the timed frames and over the sampled ones
		var time = results.IndexOf( "GPUTime" );
		if ( time >= 0 )
		{
			text.AppendLine( "scope                                     GPUTime ms  timeline ms (timed)  timeline ms (sampled)" );
			foreach ( var s in results.Scopes.Take( 25 ) )
			{
				var gpa = results.Counters[time].Unit == GpuCounterUnit.Nanoseconds ? s.Values[time] / 1e6 : s.Values[time];
				text.AppendLine( FormattableString.Invariant( $"{(s.Name.Length > 40 ? s.Name[..39] + "~" : s.Name),-40}  {gpa,10:0.000}  {Ms( timed, s.Name ),19}  {Ms( during, s.Name ),21}" ) );
			}

			text.AppendLine();
		}

		var rows = results.ToCsv().Split( '\n', StringSplitOptions.RemoveEmptyEntries ).Select( l => l.TrimEnd( '\r' ) ).ToList();
		if ( csv.Length == 0 ) csv.Append( "run," ).Append( rows[0] ).AppendLine( ",timeline_ms" );
		foreach ( var line in rows.Skip( 1 ) ) csv.Append( Csv( title ) ).Append( ',' ).Append( line ).Append( ',' ).AppendLine( Ms( timed, ScopeName( line ) ) );

		return text.ToString();
	}

	static string Ms( Dictionary<string, double> ms, string name ) => ms.TryGetValue( name, out var v ) ? v.ToString( "0.000", CultureInfo.InvariantCulture ) : "-";

	static string Csv( string text ) => '"' + text.Replace( "\"", "\"\"" ) + '"';

	static string ScopeName( string csvLine )
	{
		// The first field, quoted
		var end = csvLine.IndexOf( "\",", 1, StringComparison.Ordinal );
		return end > 0 ? csvLine[1..end].Replace( "\"\"", "\"" ) : "";
	}

	/// <summary>Every counter the device has, as a CSV: <c>-gpucounters list</c>.</summary>
	public static void WriteList( GpuCounters counters, string path )
	{
		var csv = new StringBuilder( "index,name,group,unit,discrete,description\n" );
		foreach ( var c in counters.Available )
			csv.AppendLine( string.Join( ',', c.Index.ToString( CultureInfo.InvariantCulture ), Csv( c.Name ), Csv( c.Group ), c.Unit, c.Discrete, Csv( c.Description ) ) );
		Directory.CreateDirectory( Path.GetDirectoryName( path ) );
		File.WriteAllText( path, csv.ToString() );
		Console.WriteLine( $"GPU counters: {counters.Available.Count} on the device, listed in {path}" );
	}

	/// <summary>The GPU, the library and the counters sampled, in a line.</summary>
	public static string Describe( GpuCounters counters )
	{
		var (name, generation, waves, vgprs, lds) = counters.Hardware;
		return $"GPU counters on {name}, {generation} ({waves} wave slots, {vgprs} VGPRs, {lds >> 10} KiB LDS), GPUPerfAPI from {counters.Library}: "
			+ $"{counters.Counters.Count} counters ({string.Join( ", ", counters.Counters.Select( c => c.Name ) )}) in {counters.PassCount} passes"
			+ (counters.Missing.Count > 0 ? $"; not on this GPU: {string.Join( ", ", counters.Missing )}" : "");
	}
}
