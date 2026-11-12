using Basalt.Backend;
using System.Globalization;
using System.Text;

namespace Sandbox.BasaltLab.Maps;

/// <summary>
/// <see cref="PassTimer"/>'s frames added up: GPU time per layer (its shares summed), per kind of list, and between lists,
/// with what each layer assembled and shaded when the timer counted statistics.
/// </summary>
internal sealed class PassTotals
{
	sealed class Total
	{
		public string Name;
		public PassTimer.ScopeKind Kind;
		public double Ms, Start;
		public long Scopes;
		public double Primitives, Vertices, Fragments;
		public long Draws, Pipelines, Sets, RenderScopes;
	}

	readonly Dictionary<(string, PassTimer.ScopeKind), Total> totals = new();
	readonly Dictionary<string, int> occurrences = new();
	int frames;
	double span, inLists, inPasses;

	public void Clear()
	{
		totals.Clear();
		frames = 0;
		span = inLists = inPasses = 0;
	}

	public void Add( List<PassTimer.Timing> timings )
	{
		if ( timings.Count == 0 ) return;
		frames++;

		double first = double.MaxValue, last = 0;
		occurrences.Clear();
		string previous = null;
		foreach ( var t in timings )
		{
			first = Math.Min( first, t.Start );
			last = Math.Max( last, t.End );
			if ( t.Kind == PassTimer.ScopeKind.List ) inLists += t.Ms;
			else inPasses += t.Ms;

			// A layer's shares are consecutive; the same name again after another layer (the 3D skybox's) is another row
			var name = t.Name;
			if ( t.Kind != PassTimer.ScopeKind.List )
			{
				if ( name != previous ) occurrences[name] = occurrences.GetValueOrDefault( name ) + 1;
				previous = t.Name;
				if ( occurrences[name] > 1 ) name = $"{name} ({occurrences[name]})";
			}

			if ( !totals.TryGetValue( (name, t.Kind), out var total ) ) totals[(name, t.Kind)] = total = new Total { Name = name, Kind = t.Kind };
			total.Ms += t.Ms;
			total.Start += t.Start;
			total.Scopes++;
			total.Primitives += t.Counts.Primitives;
			total.Vertices += t.Counts.VertexInvocations;
			total.Fragments += t.Counts.FragmentInvocations;
			total.Draws += t.Recorded.Draws;
			total.Pipelines += t.Recorded.Pipelines;
			total.Sets += t.Recorded.Sets;
			total.RenderScopes += t.Recorded.Scopes;
		}

		span += last - first;
	}

	IEnumerable<Total> Ordered => totals.Values.OrderBy( t => t.Kind == PassTimer.ScopeKind.List ? 1 : 0 ).ThenBy( t => t.Start / t.Scopes );

	public string Describe( string title, int pixels, bool statistics )
	{
		var sb = new StringBuilder();
		if ( frames == 0 ) return $"{title}: no frames timed by pass";

		var lists = totals.Values.Where( t => t.Kind == PassTimer.ScopeKind.List ).Sum( t => t.Scopes ) / (double)frames;
		sb.AppendLine( FormattableString.Invariant( $"{title}: {span / frames:0.000} ms of GPU from the first list to the copy's end, over {frames} frames. {lists:0.0} lists a frame, {inLists / frames:0.000} ms in them and {(span - inLists) / frames:0.000} ms between them; {inPasses / frames:0.000} ms in timed passes" ) );
		sb.AppendLine();
		sb.AppendLine( "| Scope | Kind | ms | Scopes/frame | Draws | Pipelines | Sets | Rendering scopes |" + (statistics ? " Primitives | Vertex invocations | Fragment invocations | Per pixel |" : "") );
		sb.AppendLine( "|---|---|---|---|---|---|---|---|" + (statistics ? "---|---|---|---|" : "") );
		foreach ( var t in Ordered )
		{
			var line = FormattableString.Invariant( $"| {t.Name} | {t.Kind} | {t.Ms / frames:0.000} | {t.Scopes / (double)frames:0.0} | {t.Draws / (double)frames:0} | {t.Pipelines / (double)frames:0} | {t.Sets / (double)frames:0} | {t.RenderScopes / (double)frames:0} |" );
			if ( statistics ) line += FormattableString.Invariant( $" {t.Primitives / frames:#,0} | {t.Vertices / frames:#,0} | {t.Fragments / frames:#,0} | {t.Fragments / frames / pixels:0.00} |" );
			sb.AppendLine( line );
		}

		return sb.ToString();
	}

	public void AppendCsv( StringBuilder csv, string view, string variant )
	{
		if ( frames == 0 ) return;
		foreach ( var t in Ordered )
		{
			csv.AppendLine( string.Join( ',', view, variant, '"' + t.Name + '"', t.Kind,
				(t.Ms / frames).ToString( "0.0000", CultureInfo.InvariantCulture ),
				(t.Scopes / (double)frames).ToString( "0.00", CultureInfo.InvariantCulture ),
				(t.Primitives / frames).ToString( "0", CultureInfo.InvariantCulture ),
				(t.Vertices / frames).ToString( "0", CultureInfo.InvariantCulture ),
				(t.Fragments / frames).ToString( "0", CultureInfo.InvariantCulture ),
				(t.Draws / (double)frames).ToString( "0", CultureInfo.InvariantCulture ),
				(t.Pipelines / (double)frames).ToString( "0", CultureInfo.InvariantCulture ),
				(t.Sets / (double)frames).ToString( "0", CultureInfo.InvariantCulture ),
				(t.RenderScopes / (double)frames).ToString( "0", CultureInfo.InvariantCulture ) ) );
		}

		csv.AppendLine( string.Join( ',', view, variant, "\"(between lists)\"", "Gap", ((span - inLists) / frames).ToString( "0.0000", CultureInfo.InvariantCulture ), "", "", "", "", "", "", "", "" ) );
	}
}
