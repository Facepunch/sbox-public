using System.Diagnostics;
using System.IO;
using System.Threading;
using Sandbox.Engine.Settings;
using Sandbox.Modals;
using Sandbox.Utility;

namespace Sandbox.Engine;

[SkipHotload]
internal static class BenchmarkOrchestrator
{
	private readonly record struct BenchmarkPackage( string PackageName, Dictionary<string, string> GameSettings = null );

	private static int _currentIndex = 0;

	private static readonly List<BenchmarkPackage> _allPackages = new()
	{
		new BenchmarkPackage( "facepunch.benchmark" ),
		new BenchmarkPackage( "facepunch.sbdm", new Dictionary<string, string> { { "sbdm.dev.benchmark", "1" } } ),
	};

	private static List<BenchmarkPackage> _activePackages = new();
	private static string[] _testFilter;
	private static string _repeatArg;
	private static string _durationArg;
	private static Process _tracyCapture;
	private static string _tracyOutputPath;
	private static string _tracyExePath;

	internal static string ExportPath { get; private set; }
	internal static bool IsRunning { get; set; }
	internal static List<BenchmarkTestSummary> Summaries { get; } = new();
	internal static Guid LastBatchId { get; set; }

	private static RenderSettings.VideoModeSnapshot _preRunSnapshot;

	/// <summary>
	/// Called from Bootstrap when running as benchmark.exe. Parses CLI flags and starts the first package.
	/// </summary>
	internal static void InitFromCli()
	{
		if ( !Api.IsConnected )
		{
			Log.Warning( "Not connected to backend - quitting." );
			Environment.Exit( 10 );
		}

		var packageNames = ParseFilter( CommandLine.GetSwitch( "+benchmarks", null ) );
		_activePackages = FilterPackages( packageNames );

		_testFilter = ParseFilter( CommandLine.GetSwitch( "+benchmark-tests", null ) );
		_repeatArg = CommandLine.GetSwitch( "+benchmark-repeat", null );
		_durationArg = CommandLine.GetSwitch( "+benchmark-duration", null );
		ExportPath = CommandLine.GetSwitch( "+benchmark-export", null );

		RenderSettings.Instance.ApplySettingsForBenchmarks();

		if ( CommandLine.HasSwitch( "+benchmark-tracy" ) && !string.IsNullOrEmpty( ExportPath ) )
		{
			_tracyOutputPath = Path.ChangeExtension( ExportPath, ".tracy" );
			_tracyExePath = CommandLine.GetSwitch( "+benchmark-tracy-exe", null );
		}

		if ( !TryLoadNextPackage() )
		{
			Console.WriteLine( "Quitting" );
			ConVarSystem.Run( "quit" );
		}
	}

	/// <summary>
	/// Starts a benchmark run from within the game. Results panel shown when complete.
	/// </summary>
	internal static void Run( string[] packageFilter = null, string[] testFilter = null )
	{
		IsRunning = true;
		_currentIndex = 0;
		Summaries.Clear();
		LastBatchId = Guid.Empty;
		_testFilter = testFilter?.Length > 0 ? testFilter : null;
		_repeatArg = null;
		_durationArg = null;
		ExportPath = null;

		_activePackages = FilterPackages( packageFilter );

		_preRunSnapshot = RenderSettings.Instance.CaptureSnapshot();
		RenderSettings.Instance.ApplySettingsForBenchmarks();

		if ( !TryLoadNextPackage() )
		{
			// No package matched the filter - nothing will load, so undo the benchmark state we just applied.
			RestoreSettings();
			IsRunning = false;
		}
	}

	private static List<BenchmarkPackage> FilterPackages( string[] packageNames )
	{
		if ( packageNames is not { Length: > 0 } )
			return _allPackages.ToList();

		var wanted = new HashSet<string>( packageNames, StringComparer.OrdinalIgnoreCase );
		return _allPackages.Where( x => wanted.Contains( x.PackageName ) ).ToList();
	}

	internal static void RestoreSettings()
	{
		RenderSettings.Instance.RestoreSnapshot( _preRunSnapshot );
	}

	internal static bool TryLoadNextPackage()
	{
		if ( _currentIndex >= _activePackages.Count ) return false;

		var pkg = _activePackages[_currentIndex];
		var settings = new Dictionary<string, string>( pkg.GameSettings ?? new() );
		if ( _testFilter?.Length > 0 )
			settings["benchmark.tests"] = string.Join( ",", _testFilter );
		if ( !string.IsNullOrEmpty( _repeatArg ) )
			settings["benchmark.repeat"] = _repeatArg;
		if ( !string.IsNullOrEmpty( _durationArg ) )
			settings["benchmark.duration"] = _durationArg;

		// Show the loading screen while the package loads, same as MenuUtility's normal game-open path
		LoadingScreen.IsVisible = true;
		LoadingScreen.Media = null;
		LoadingScreen.Title = "Loading Benchmark";

		LaunchArguments.GameSettings = settings;
		_ = IGameInstanceDll.Current.LoadGamePackageAsync( pkg.PackageName, GameLoadingFlags.Host, default );
		_currentIndex++;

		return true;
	}

	internal static void EnsureTracyCaptureStarted()
	{
		if ( string.IsNullOrEmpty( _tracyOutputPath ) || _tracyCapture != null ) return;
		StartTracyCapture( _tracyOutputPath, _tracyExePath );
	}

	private static void StartTracyCapture( string outputPath, string captureExe = null )
	{
		string exePath;
		if ( !string.IsNullOrEmpty( captureExe ) )
		{
			exePath = captureExe;
		}
		else
		{
			var exeDir = Path.GetDirectoryName( Environment.ProcessPath ) ?? ".";
			var localExe = Path.Combine( exeDir, "tracy-capture.exe" );
			exePath = File.Exists( localExe ) ? localExe : "tracy-capture.exe";
		}

		var startInfo = new ProcessStartInfo
		{
			FileName = exePath,
			Arguments = $"-o \"{outputPath}\" -f",
			UseShellExecute = false,
			CreateNoWindow = true,
		};

		try
		{
			_tracyCapture = Process.Start( startInfo );
			Log.Info( $"[Benchmark] Tracy capture started → {outputPath}" );
			Thread.Sleep( 1000 );
		}
		catch ( Exception ex )
		{
			Log.Warning( $"[Benchmark] Could not start tracy-capture: {ex.Message}" );
		}
	}

	private static string[] ParseFilter( string value ) =>
		string.IsNullOrEmpty( value ) ? null : value.Split( ',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries );
}
