using System.Text;
using System.Text.Json;

namespace Facepunch;

/// <summary>
/// How SboxBuild presents itself on GitHub Actions: collapsible log groups, and a job summary built
/// from what each step recorded. Everything here does nothing outside Actions.
/// </summary>
internal static class GitHubActions
{
	internal record TestCount( string Project, int Passed, int Failed, int Skipped );
	internal record FailedTest( string Name, string Message, string Location );
	internal record Link( string Label, string Url );

	private record StepRecord( string Title, double Seconds, bool Success, List<string> Notes, List<TestCount> Tests, List<FailedTest> Failures, List<Link> Links );

	private static readonly object sync = new();
	private static string openGroup;
	private static readonly List<string> notes = new();
	private static readonly List<TestCount> tests = new();
	private static readonly List<FailedTest> failures = new();
	private static readonly List<Link> links = new();

	/// <summary>
	/// Row names in the job summary for verbs that don't set <see cref="Title"/> themselves.
	/// </summary>
	private static readonly Dictionary<string, string> VerbTitles = new()
	{
		["pr-native"] = "Native",
		["build-shaders"] = "Shaders",
		["build-content"] = "Content",
		["build-addons"] = "Addons",
		["test"] = "Tests",
		["format"] = "Format",
		["publish-build"] = "Publish build",
		["write-version"] = "Version",
		["sign-binaries"] = "Sign binaries",
		["nvpatch"] = "NvPatch",
		["upload-build-artifacts"] = "Upload build",
		["upload-symbols"] = "Symbols",
		["upload-docs"] = "Docs",
		["sentry-release"] = "Sentry release",
		["upload-steam"] = "Steam upload",
		["discord-post"] = "Discord",
		["report-build"] = "Report build",
		["sync-public-repo"] = "Sync public repo",
	};

	internal static bool Enabled => Utility.IsCi();

	/// <summary>
	/// This invocation's row in the job summary. Commands whose row depends on their options set it.
	/// </summary>
	internal static string Title { get; set; }

	private static string RecordsPath
	{
		get
		{
			// Emptied at the start and end of every job, so records never leak between jobs.
			var temp = Environment.GetEnvironmentVariable( "RUNNER_TEMP" );
			return string.IsNullOrEmpty( temp ) ? null : Path.Combine( temp, "sboxbuild-summary.jsonl" );
		}
	}

	/// <summary>
	/// Starts a collapsible log section, ending the one before it. Actions doesn't nest them.
	/// </summary>
	internal static void Group( string title )
	{
		if ( !Enabled ) return;
		title = Log.TerminalText( title ).ReplaceLineEndings( " " ).Trim();
		if ( title.Length == 0 ) return;

		bool end;
		lock ( sync )
		{
			if ( openGroup == title ) return;
			end = openGroup != null;
			openGroup = title;
		}

		if ( end ) Log.Raw( "::endgroup::" );
		Log.Raw( $"::group::{title}" );
	}

	internal static void EndGroup()
	{
		if ( !Enabled ) return;
		lock ( sync )
		{
			if ( openGroup == null ) return;
			openGroup = null;
		}
		Log.Raw( "::endgroup::" );
	}

	/// <summary>
	/// An error annotation: listed on the run and the PR's checks, and shown on the diff when the file
	/// is part of it. <paramref name="file"/> is relative to the repository root.
	/// </summary>
	internal static void Error( string title, string message, string file = null, string line = null )
	{
		if ( !Enabled ) return;

		var properties = new List<string> { $"title={EscapeProperty( title )}" };
		if ( !string.IsNullOrEmpty( file ) ) properties.Add( $"file={EscapeProperty( file.Replace( '\\', '/' ) )}" );
		if ( !string.IsNullOrEmpty( line ) ) properties.Add( $"line={EscapeProperty( line )}" );

		Log.Raw( $"::error {string.Join( ',', properties )}::{EscapeData( message )}" );
	}

	private static string EscapeData( string text ) => text.Replace( "%", "%25" ).Replace( "\r", "%0D" ).Replace( "\n", "%0A" );
	private static string EscapeProperty( string text ) => EscapeData( text ).Replace( ":", "%3A" ).Replace( ",", "%2C" );

	/// <summary>
	/// A short fact about how this step went, shown beside it in the job summary.
	/// </summary>
	internal static void Note( string note )
	{
		lock ( sync ) notes.Add( note );
	}

	internal static void AddTests( TestCount count )
	{
		lock ( sync ) tests.Add( count );
	}

	internal static void AddFailure( FailedTest failure )
	{
		lock ( sync ) failures.Add( failure );
	}

	internal static void AddLink( string label, string url )
	{
		lock ( sync ) links.Add( new Link( label, url ) );
	}

	/// <summary>
	/// Appends this invocation to the records the summary command renders at the end of the job.
	/// </summary>
	internal static void RecordStep( string verb, TimeSpan elapsed, bool success )
	{
		if ( !Enabled || RecordsPath is not { } path ) return;
		if ( verb is null or "summary" or "notify-slack" || verb.StartsWith( '-' ) ) return;

		var title = Title ?? VerbTitles.GetValueOrDefault( verb, verb );
		StepRecord record;
		lock ( sync ) record = new StepRecord( title, elapsed.TotalSeconds, success, [.. notes], [.. tests], [.. failures], [.. links] );

		try
		{
			File.AppendAllText( path, JsonSerializer.Serialize( record ) + "\n" );
		}
		catch ( Exception ex )
		{
			Log.Warning( $"Couldn't record this step for the job summary: {ex.Message}" );
		}
	}

	/// <summary>
	/// Writes the job summary from every step recorded so far in this job.
	/// </summary>
	internal static void WriteSummary()
	{
		var summaryPath = Environment.GetEnvironmentVariable( "GITHUB_STEP_SUMMARY" );
		if ( string.IsNullOrEmpty( summaryPath ) || RecordsPath is not { } path || !File.Exists( path ) )
		{
			Log.Info( "Nothing to summarise." );
			return;
		}

		var records = File.ReadAllLines( path )
			.Where( line => !string.IsNullOrWhiteSpace( line ) )
			.Select( line => JsonSerializer.Deserialize<StepRecord>( line ) )
			.ToList();

		File.AppendAllText( summaryPath, RenderSummary( records ) );
	}

	private static string RenderSummary( List<StepRecord> records )
	{
		var markdown = new StringBuilder();

		var counts = records.SelectMany( x => x.Tests ).ToList();
		if ( counts.Count > 0 )
		{
			var failed = counts.Sum( x => x.Failed );
			markdown.Append( $"**{counts.Sum( x => x.Passed ):N0} passed** · " );
			markdown.Append( failed > 0 ? $"**{failed:N0} failed**" : "0 failed" );
			markdown.AppendLine( $" · {counts.Sum( x => x.Skipped ):N0} skipped" );
			markdown.AppendLine();
		}

		markdown.AppendLine( "| Step | Time | |" );
		markdown.AppendLine( "| :-- | --: | :-- |" );
		foreach ( var record in records )
		{
			var details = new List<string>();
			if ( !record.Success ) details.Add( "**failed**" );
			details.AddRange( record.Notes );
			if ( record.Tests.Count > 0 )
			{
				details.Add( string.Join( " · ", record.Tests
					.GroupBy( x => x.Project )
					.Select( x => (Project: x.Key, Passed: x.Sum( y => y.Passed )) )
					.Where( x => x.Passed > 0 )
					.OrderByDescending( x => x.Passed )
					.Select( x => $"{x.Project} {x.Passed:N0}" ) ) );
			}

			markdown.AppendLine( $"| {Escape( record.Title )} | {FormatTime( record.Seconds )} | {Escape( string.Join( " · ", details ) )} |" );
		}

		var failures = records.SelectMany( x => x.Failures ).ToList();
		if ( failures.Count > 0 )
		{
			markdown.AppendLine();
			markdown.AppendLine( "**Failed tests**" );
			markdown.AppendLine();
			foreach ( var failure in failures.Take( 25 ) )
			{
				var location = string.IsNullOrEmpty( failure.Location ) ? "" : $" · {Escape( failure.Location )}";
				markdown.AppendLine( $"- `{failure.Name.Replace( "`", "'" )}`{location}" );
				if ( !string.IsNullOrEmpty( failure.Message ) )
					markdown.AppendLine( $"  {Escape( Truncate( failure.Message, 400 ) )}" );
			}
			if ( failures.Count > 25 )
				markdown.AppendLine( $"- and {failures.Count - 25} more, see the Tests log" );
		}

		foreach ( var link in records.SelectMany( x => x.Links ) )
		{
			markdown.AppendLine();
			markdown.AppendLine( $"{Escape( link.Label )}: [{Escape( link.Url )}]({link.Url})" );
		}

		return markdown.ToString();
	}

	/// <summary>
	/// Keeps text from being read as Markdown tables or HTML: assert messages are full of &lt;T&gt;.
	/// </summary>
	private static string Escape( string text ) => text
		.Replace( "&", "&amp;" ).Replace( "<", "&lt;" ).Replace( ">", "&gt;" ).Replace( "|", "\\|" )
		.ReplaceLineEndings( " " );

	private static string Truncate( string text, int length ) => text.Length <= length ? text : text[..length] + "…";

	private static string FormatTime( double seconds )
	{
		var time = TimeSpan.FromSeconds( seconds );
		return time.TotalMinutes >= 1 ? $"{(int)time.TotalMinutes}m {time.Seconds}s" : $"{time.Seconds}s";
	}
}
