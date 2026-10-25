using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using static Facepunch.Constants;

namespace Facepunch.Steps;

internal partial class Test( bool noBuild = true, string filter = null )
{
	/// <summary>
	/// Filter for pull request runs: everything except tests that talk to the live backend, so a
	/// backend hiccup can't fail an unrelated PR, and tests tagged <c>Slow</c> (full exports, second
	/// engine boots). Master builds still run the Slow tests.
	/// </summary>
	public const string PullRequestFilter = "TestCategory!=LiveBackend&TestCategory!=Slow";

	/// <param name="Name">The project under engine/Tests/.</param>
	/// <param name="NeedsEngine">Whether FACEPUNCH_ENGINE is set for it.</param>
	/// <param name="Shard">Label for one part of a project split across processes, or null.</param>
	/// <param name="ShardFilter">Which of the project's tests this part runs.</param>
	/// <param name="Isolated">Runs one test at a time, with tiered compilation off.</param>
	private record TestProject( string Name, bool NeedsEngine, string Shard = null, string ShardFilter = null, bool Isolated = false )
	{
		public string DisplayName => Shard is null ? Name : $"{Name} ({Shard})";
	}

	/// <summary>
	/// Allocation tests (see engine/Tests/Shared/Allocations.cs) run in a process of their own, one at a
	/// time, so other tests' threads aren't allocating and collecting around them. Tiered compilation is
	/// off there, so everything is compiled once, fully optimised, rather than recompiled mid-measurement.
	/// </summary>
	private const string AllocationTests = "TestCategory=Allocations";
	private const string NotAllocationTests = "TestCategory!=Allocations";

	/// <summary>
	/// The Integration tests that compile whole addons (~20s together) or export a standalone game
	/// (~20s, master only). They run in their own process alongside the rest. Each process boots its
	/// own engine (~10s).
	/// </summary>
	private const string IntegrationCompileTests = "FullyQualifiedName~ShaderGraphTests.|FullyQualifiedName~AddonTests.BuiltInTest.|FullyQualifiedName~StandaloneTests.";
	private const string IntegrationOtherTests = "FullyQualifiedName!~ShaderGraphTests.&FullyQualifiedName!~AddonTests.BuiltInTest.&FullyQualifiedName!~StandaloneTests.";

	/// <summary>
	/// The test projects. They run at the same time, so the whole run takes as long as the slowest one
	/// (Sandbox.Test.Integration, which is split in two, plus its allocation tests). Sandbox.Test.Unit and Sandbox.Test.Text run with FACEPUNCH_ENGINE
	/// removed from their environment: those tiers must work without a built native engine, so a test
	/// that sneaks in a native dependency fails here rather than on someone's bare checkout.
	/// New test projects under engine/Tests/ need to be added to this list.
	/// </summary>
	private static readonly TestProject[] Projects =
	[
		new( "Sandbox.Test.Integration", NeedsEngine: true, Shard: "most", ShardFilter: $"{IntegrationOtherTests}&{NotAllocationTests}" ),
		new( "Sandbox.Test.Integration", NeedsEngine: true, Shard: "compiles and exports", ShardFilter: IntegrationCompileTests ),
		new( "Sandbox.Test.Integration", NeedsEngine: true, Shard: "allocations", ShardFilter: AllocationTests, Isolated: true ),
		new( "Sandbox.Test.Unit", NeedsEngine: false, ShardFilter: NotAllocationTests ),
		new( "Sandbox.Test.Unit", NeedsEngine: false, Shard: "allocations", ShardFilter: AllocationTests, Isolated: true ),
		new( "Sandbox.Test.Engine", NeedsEngine: true, ShardFilter: NotAllocationTests ),
		new( "Sandbox.Test.Engine", NeedsEngine: true, Shard: "allocations", ShardFilter: AllocationTests, Isolated: true ),
		new( "Sandbox.Test.Text", NeedsEngine: false ),
		new( "Sandbox.Test.NetSim", NeedsEngine: true ),
	];

	/// <summary>
	/// How many of the slowest tests to list after each project, so slow tests get noticed.
	/// </summary>
	private const int SlowestTestCount = 10;

	private record TestResult( TestProject Project, bool Success, TimeSpan Duration, List<string> Output, List<string> FailedTests, List<(string Name, double Ms)> Timings );

	internal ExitCode Run()
	{
		try
		{
			string rootDir = Directory.GetCurrentDirectory();
			string engineDir = Path.Combine( rootDir, "engine" );
			string gameDir = Path.Combine( rootDir, "game" );

			Log.Info( $"Running {Projects.Length} test processes at the same time..." );

			var tasks = Projects
				.Select( project => Task.Factory.StartNew( () => RunTestProject( project, engineDir, gameDir ), TaskCreationOptions.LongRunning ) )
				.ToArray();

			// Print each project's output as one block when it finishes, rather than interleaving them.
			var results = new List<TestResult>();
			var pending = tasks.ToList();
			while ( pending.Count > 0 )
			{
				var index = Task.WaitAny( pending.ToArray() );
				var result = pending[index].Result;
				pending.RemoveAt( index );
				results.Add( result );
				PrintResult( result );
			}

			Log.Info( "" );
			Log.Info( "Test summary:" );
			foreach ( var result in results.OrderBy( x => Array.IndexOf( Projects, x.Project ) ) )
			{
				Log.Info( $"  {result.Project.DisplayName}: {(result.Success ? "passed" : "FAILED")} in {result.Duration.TotalSeconds:F0}s" );
			}

			var failed = results.Where( x => !x.Success ).ToList();
			foreach ( var result in failed )
			{
				Log.Error( $"{result.Project.DisplayName} tests failed!" );
			}

			if ( failed.Count > 0 )
				return ExitCode.Failure;

			Log.Info( "All tests completed successfully!" );
			return ExitCode.Success;
		}
		catch ( Exception ex )
		{
			Log.Error( $"Test operations failed with error: {ex}" );
			return ExitCode.Failure;
		}
	}

	/// <summary>
	/// Runs a single test project with the environment its tier expects, collecting its output and
	/// the duration of every test.
	/// </summary>
	private TestResult RunTestProject( TestProject project, string engineDir, string gameDir )
	{
		var csproj = Path.Combine( "Tests", project.Name, $"{project.Name}.csproj" );

		// --no-build: BuildManaged already compiled all projects in Sandbox-Engine.slnx (including test projects).
		var noBuildFlag = noBuild ? " --no-build" : string.Empty;
		var filters = new[] { filter, project.ShardFilter }.Where( x => !string.IsNullOrEmpty( x ) ).Select( x => $"({x})" ).ToArray();
		var filterFlag = filters.Length == 0 ? string.Empty : $" --filter \"{string.Join( "&", filters )}\"";
		// Run settings after the --, overriding the assembly's [Parallelize]
		var isolatedFlag = project.Isolated ? " -- MSTest.Parallelize.Workers=1" : string.Empty;
		var testArgs = $"test {csproj} --logger \"console;verbosity=normal;consoleLoggerParameters=ErrorsOnly\" -c Release{noBuildFlag}{filterFlag}{isolatedFlag}";

		var output = new List<string>();
		var failedTests = new List<string>();
		var timings = new List<(string, double)>();
		StringBuilder currentFailedTestInfo = new();
		var isCollectingFailedTestInfo = false;

		var timer = Stopwatch.StartNew();
		bool success = Utility.RunProcess(
			"dotnet",
			testArgs,
			engineDir,
			new Dictionary<string, string>
			{
				{ "FACEPUNCH_ENGINE", project.NeedsEngine ? gameDir : null },
				{ "DOTNET_TieredCompilation", project.Isolated ? "0" : null },
			},
			onDataReceived: ( sender, e ) =>
			{
				if ( e.Data == null )
					return;

				lock ( output )
				{
					output.Add( e.Data );

					var timing = TestDuration().Match( e.Data );
					if ( timing.Success )
					{
						timings.Add( (timing.Groups["name"].Value, ParseDuration( timing.Groups["duration"].Value )) );
					}

					// A bit hacky but we collect failed tests to get a nicer summary in the end
					if ( isCollectingFailedTestInfo && e.Data.TrimStart().StartsWith( "Passed" ) )
					{
						failedTests.Add( currentFailedTestInfo.ToString().Trim( '\n' ) );
						currentFailedTestInfo.Clear();
						isCollectingFailedTestInfo = false;
					}
					if ( e.Data.TrimStart().StartsWith( "Failed " ) )
					{
						isCollectingFailedTestInfo = true;
					}
					if ( isCollectingFailedTestInfo )
					{
						currentFailedTestInfo.AppendLine( e.Data );
					}
				}
			}
		);

		lock ( output )
		{
			if ( isCollectingFailedTestInfo )
			{
				failedTests.Add( currentFailedTestInfo.ToString().Trim( '\n' ) );
			}

			return new TestResult( project, success, timer.Elapsed, output, failedTests, timings );
		}
	}

	private static void PrintResult( TestResult result )
	{
		var project = result.Project;
		RecordForSummary( result );

		GitHubActions.Group( $"{project.DisplayName} ({(result.Success ? "passed" : "failed")}, {result.Duration.TotalSeconds:F0}s)" );
		Log.Info( "" );
		Log.Info( project.NeedsEngine
			? $"===== {project.DisplayName} ({result.Duration.TotalSeconds:F0}s) ====="
			: $"===== {project.DisplayName} ({result.Duration.TotalSeconds:F0}s, FACEPUNCH_ENGINE removed - this tier must not need the native engine) =====" );

		foreach ( var line in result.Output )
		{
			Log.Info( line );
		}

		if ( result.Timings.Count > 0 )
		{
			Log.Info( "" );
			Log.Info( $"Slowest tests ({project.DisplayName}):" );
			foreach ( var (name, ms) in result.Timings.OrderByDescending( x => x.Ms ).Take( SlowestTestCount ) )
			{
				Log.Info( $"  {ms / 1000.0,7:F2}s  {name}" );
			}
		}

		// Failures stay outside the collapsed output so they're readable straight away.
		GitHubActions.EndGroup();

		if ( !result.Success )
		{
			Log.Info( "" );
			Log.Info( $"Failed Tests Summary ({project.DisplayName}):" );
			Log.Info( "" );

			foreach ( var failedTest in result.FailedTests )
			{
				Log.Info( failedTest );
				Log.Info( "" );
			}
		}
	}

	/// <summary>
	/// Hands the counts from dotnet test's closing totals, and each failure's message and location,
	/// to the job summary.
	/// </summary>
	private static void RecordForSummary( TestResult result )
	{
		int Count( Regex pattern ) => result.Output
			.Select( line => pattern.Match( line ) )
			.Where( match => match.Success )
			.Sum( match => int.Parse( match.Groups["count"].Value, CultureInfo.InvariantCulture ) );

		var name = result.Project.Name.Replace( "Sandbox.Test.", "" );
		GitHubActions.AddTests( new( name, Count( PassedCount() ), Count( FailedCount() ), Count( SkippedCount() ) ) );

		// A collected block runs until the next passing test, so back-to-back failures share one.
		var blocks = new List<string>();
		foreach ( var line in result.FailedTests.SelectMany( x => x.Split( '\n' ) ) )
		{
			if ( FailedName().IsMatch( line ) || blocks.Count == 0 ) blocks.Add( "" );
			blocks[^1] += line + "\n";
		}

		foreach ( var failure in blocks )
		{
			var lines = failure.Split( '\n' ).Select( x => x.Trim() ).ToList();
			var method = StackMethod().Match( failure );
			var location = StackLocation().Match( failure );
			var test = method.Success
				? string.Join( '.', method.Groups["method"].Value.Split( '.' ).TakeLast( 2 ) )
				: FailedName().Match( failure ) is { Success: true } header ? header.Groups["name"].Value : lines.FirstOrDefault() ?? "";

			var message = lines
				.SkipWhile( x => x != "Error Message:" ).Skip( 1 )
				.TakeWhile( x => x != "Stack Trace:" && x.Length > 0 );

			var text = string.Join( ' ', message );
			var file = location.Success ? location.Groups["file"].Value : null;
			var line = location.Success ? location.Groups["line"].Value : null;

			GitHubActions.AddFailure( new( test, text, file is null ? null : $"{Path.GetFileName( file )}:{line}" ) );
			GitHubActions.Error( $"Test failed: {test}", text.Length > 0 ? text : "See the Tests log.",
				file is null ? null : Path.GetRelativePath( Directory.GetCurrentDirectory(), file ), line );
		}
	}

	/// <summary>
	/// Parses a console logger duration such as "< 1 ms", "250 ms", "3 s" or "1 m 2 s" into milliseconds.
	/// </summary>
	private static double ParseDuration( string text )
	{
		if ( text.TrimStart().StartsWith( '<' ) )
			return 0;

		double total = 0;
		foreach ( Match part in DurationPart().Matches( text ) )
		{
			var value = double.Parse( part.Groups["value"].Value, CultureInfo.InvariantCulture );
			total += part.Groups["unit"].Value switch
			{
				"ms" => value,
				"s" => value * 1000,
				"m" => value * 60_000,
				"h" => value * 3_600_000,
				_ => 0,
			};
		}
		return total;
	}

	[GeneratedRegex( @"^\s+(?:Passed|Failed) (?<name>.+?) \[(?<duration>[^\]]+)\]\s*$" )]
	private static partial Regex TestDuration();

	[GeneratedRegex( @"(?<value>[\d.]+)\s*(?<unit>ms|s|m|h)\b" )]
	private static partial Regex DurationPart();

	[GeneratedRegex( @"^\s*Passed:\s*(?<count>\d+)\s*$" )]
	private static partial Regex PassedCount();

	[GeneratedRegex( @"^\s*Failed:\s*(?<count>\d+)\s*$" )]
	private static partial Regex FailedCount();

	[GeneratedRegex( @"^\s*Skipped:\s*(?<count>\d+)\s*$" )]
	private static partial Regex SkippedCount();

	[GeneratedRegex( @"^\s*Failed (?<name>.+?) \[" )]
	private static partial Regex FailedName();

	[GeneratedRegex( @"\bat (?<method>[\w.`<>]+)\(" )]
	private static partial Regex StackMethod();

	[GeneratedRegex( @" in (?<file>.+?):line (?<line>\d+)" )]
	private static partial Regex StackLocation();
}
