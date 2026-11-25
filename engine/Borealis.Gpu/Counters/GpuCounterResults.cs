using System.Globalization;
using System.Linq;
using System.Text;

namespace Borealis.Gpu;

/// <summary>
/// <see cref="GpuCounters"/>' results: per scope name, each counter's mean per frame over the samples read. A name that
/// occurs several times in a frame (a pass per view) adds up: times, counts and bytes are summed, percentages and ratios
/// are weighted by each occurrence's GPU time.
/// </summary>
public sealed class GpuCounterResults
{
	/// <summary>One scope name's counters.</summary>
	/// <param name="Name">The scope's name, as the GPU timeline shows it.</param>
	/// <param name="Occurrences">How many times it ran per frame.</param>
	/// <param name="Values">Each counter's value, in <see cref="Counters"/>' order.</param>
	public sealed record Scope( string Name, int Occurrences, double[] Values );

	/// <summary>The counters, in the order of each scope's <see cref="Scope.Values"/>.</summary>
	public IReadOnlyList<GpuCounterInfo> Counters { get; }

	/// <summary>The scopes, longest GPU time first.</summary>
	public IReadOnlyList<Scope> Scopes { get; }

	/// <summary>How many samples (each <see cref="GpuCounters.PassCount"/> frames) the means are over.</summary>
	public int Samples { get; }

	GpuCounterResults( IReadOnlyList<GpuCounterInfo> counters, IReadOnlyList<Scope> scopes, int samples )
	{
		Counters = counters;
		Scopes = scopes;
		Samples = samples;
	}

	/// <summary>The index of counter <paramref name="name"/> in <see cref="Counters"/>, -1 if it wasn't sampled.</summary>
	public int IndexOf( string name )
	{
		for ( int i = 0; i < Counters.Count; i++ ) if ( Counters[i].Name.Equals( name, StringComparison.OrdinalIgnoreCase ) ) return i;
		return -1;
	}

	/// <summary>Scope <paramref name="scope"/>'s value of counter <paramref name="counter"/>, or NaN.</summary>
	public double Value( string scope, string counter )
	{
		var c = IndexOf( counter );
		var s = Scopes.FirstOrDefault( x => x.Name == scope );
		return c < 0 || s is null ? double.NaN : s.Values[c];
	}

	/// <summary>
	/// Put results together: per occurrence of each scope name, the sum of its values over <c>Count</c> samples.
	/// </summary>
	public static GpuCounterResults Build( IReadOnlyList<GpuCounterInfo> counters, IEnumerable<(string Name, int Occurrence, double[] Sum, int Count)> totals, int samples )
	{
		var time = -1;
		for ( int i = 0; i < counters.Count; i++ ) if ( counters[i].Name == "GPUTime" ) time = i;

		var scopes = new List<Scope>();
		foreach ( var group in totals.Where( t => t.Count > 0 ).GroupBy( t => t.Name ) )
		{
			var values = new double[counters.Count];
			var weights = new double[counters.Count];
			var occurrences = 0;
			foreach ( var (_, _, sum, count) in group )
			{
				occurrences++;
				var weight = time >= 0 ? Math.Max( sum[time] / count, 1e-9 ) : 1;
				for ( int c = 0; c < counters.Count; c++ )
				{
					var mean = sum[c] / count;
					if ( counters[c].Additive ) values[c] += mean;
					else
					{
						values[c] += mean * weight;
						weights[c] += weight;
					}
				}
			}

			for ( int c = 0; c < counters.Count; c++ ) if ( !counters[c].Additive && weights[c] > 0 ) values[c] /= weights[c];
			scopes.Add( new Scope( group.Key, occurrences, values ) );
		}

		if ( time >= 0 ) scopes.Sort( ( a, b ) => b.Values[time].CompareTo( a.Values[time] ) );
		else scopes.Sort( ( a, b ) => string.CompareOrdinal( a.Name, b.Name ) );
		return new GpuCounterResults( counters, scopes, samples );
	}

	static string Unit( GpuCounterUnit unit ) => unit switch
	{
		GpuCounterUnit.Percentage => "%",
		GpuCounterUnit.Cycles => "cycles",
		GpuCounterUnit.Milliseconds => "ms",
		GpuCounterUnit.Bytes => "bytes",
		GpuCounterUnit.Kilobytes => "KB",
		GpuCounterUnit.Nanoseconds => "ns",
		GpuCounterUnit.Items => "count",
		_ => "ratio",
	};

	/// <summary>A CSV: a row per scope, a column per counter (its unit in the header), the scope's occurrences per frame.</summary>
	public string ToCsv()
	{
		var csv = new StringBuilder();
		csv.Append( "scope,occurrences" );
		foreach ( var c in Counters ) csv.Append( ',' ).Append( c.Name ).Append( " (" ).Append( Unit( c.Unit ) ).Append( ')' );
		csv.AppendLine();
		foreach ( var s in Scopes )
		{
			csv.Append( '"' ).Append( s.Name.Replace( "\"", "\"\"" ) ).Append( "\"," ).Append( s.Occurrences );
			foreach ( var v in s.Values ) csv.Append( ',' ).Append( v.ToString( "0.######", CultureInfo.InvariantCulture ) );
			csv.AppendLine();
		}

		return csv.ToString();
	}

	/// <summary>A value as the table shows it: times in ms, bytes in KiB or MiB, counts in k or M, percentages to a tenth.</summary>
	public static string Format( GpuCounterInfo counter, double v )
	{
		if ( double.IsNaN( v ) ) return "-";
		var i = CultureInfo.InvariantCulture;
		return counter.Unit switch
		{
			GpuCounterUnit.Nanoseconds => (v / 1e6).ToString( "0.000", i ),
			GpuCounterUnit.Milliseconds => v.ToString( "0.000", i ),
			GpuCounterUnit.Percentage => v.ToString( "0.0", i ),
			GpuCounterUnit.Ratio => v.ToString( "0.00", i ),
			GpuCounterUnit.Bytes => v >= 1 << 20 ? (v / (1 << 20)).ToString( "0.0", i ) + "M" : (v / 1024).ToString( "0.0", i ) + "K",
			GpuCounterUnit.Kilobytes => v >= 1024 ? (v / 1024).ToString( "0.0", i ) + "M" : v.ToString( "0", i ) + "K",
			_ => v >= 1e6 ? (v / 1e6).ToString( "0.00", i ) + "M" : v >= 1e4 ? (v / 1e3).ToString( "0.0", i ) + "k" : v.ToString( "0", i ),
		};
	}

	static string Header( GpuCounterInfo c ) => c.Unit switch
	{
		GpuCounterUnit.Nanoseconds or GpuCounterUnit.Milliseconds => c.Name + " ms",
		GpuCounterUnit.Percentage => c.Name + " %",
		_ => c.Name,
	};

	/// <summary>
	/// The results as text tables for a log: a row per scope (the <paramref name="rows"/> longest), columns in blocks of
	/// <paramref name="columns"/> counters, each block repeating the scope names.
	/// </summary>
	public string ToTable( int rows = 40, int columns = 9 )
	{
		var text = new StringBuilder();
		var shown = Scopes.Take( rows ).ToList();
		if ( shown.Count == 0 ) return "(no scopes sampled)\n";
		var nameWidth = Math.Min( 40, shown.Max( s => s.Name.Length + (s.Occurrences > 1 ? 4 : 0) ) );
		for ( int first = 0; first < Counters.Count; first += columns )
		{
			var block = Enumerable.Range( first, Math.Min( columns, Counters.Count - first ) ).ToList();
			var widths = block.Select( c => Math.Max( Header( Counters[c] ).Length, 7 ) ).ToList();
			text.Append( "scope".PadRight( nameWidth ) );
			for ( int k = 0; k < block.Count; k++ ) text.Append( "  " ).Append( Header( Counters[block[k]] ).PadLeft( widths[k] ) );
			text.AppendLine();
			foreach ( var s in shown )
			{
				var name = s.Occurrences > 1 ? $"{s.Name} x{s.Occurrences}" : s.Name;
				if ( name.Length > nameWidth ) name = name[..(nameWidth - 1)] + "~";
				text.Append( name.PadRight( nameWidth ) );
				for ( int k = 0; k < block.Count; k++ ) text.Append( "  " ).Append( Format( Counters[block[k]], s.Values[block[k]] ).PadLeft( widths[k] ) );
				text.AppendLine();
			}

			text.AppendLine();
		}

		return text.ToString();
	}
}
