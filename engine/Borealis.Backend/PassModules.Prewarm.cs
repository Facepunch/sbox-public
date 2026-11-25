using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Sandbox.ShaderCompiler;

namespace Borealis.Backend;

public sealed partial class PassModules
{
	/// <summary>The file in the pass cache that keeps the stages draws asked for, by run.</summary>
	internal const string StagesFile = "stages.txt";

	/// <summary>Runs a stage no draw asked for is kept for.</summary>
	internal const int KeptRuns = 10;

	// Each stage asked for (module path, stage, combo values) and the last run that asked; null until Prewarm reads them
	Dictionary<string, int> kept;
	readonly HashSet<string> remembered = new( StringComparer.Ordinal );
	int run;
	int keptVersion;
	bool saving;
	Task prewarming;
	int readAhead;

	/// <summary>Stages <see cref="Prewarm"/> started reading or compiling, for tests and reports.</summary>
	internal int ReadAhead => System.Threading.Volatile.Read( ref readAhead );

	string StagesPath => Path.Combine( CacheFolder ?? Path.Combine( Path.GetDirectoryName( Core ), ".source2", "borealispasses" ), StagesFile );

	/// <summary>
	/// Read ahead, on workers, every stage the draws of the last <see cref="KeptRuns"/> runs asked for: each is read from its
	/// offline compile or the cache, or compiled when neither has it (the first run after an update), so the frames that
	/// draw them find them made and a load can wait for them behind its loading screen (<see cref="Compiling"/> counts them).
	/// From here on, the stages this run's draws ask for are kept for the next. The game's device calls it at start; tests
	/// and labs don't, so their asks aren't kept.
	/// </summary>
	public Task Prewarm()
	{
		List<(string Path, StageKind Kind, Dictionary<string, int> Values)> ahead;
		lock ( remembered )
		{
			if ( kept is not null ) return prewarming ?? Task.CompletedTask;
			ahead = ReadStages( StagesPath, out run, out keptVersion, out kept );
		}

		// Modules made from a package shader's embedded source are named by their text, which an update can change: one an
		// older compiler asked for may never be asked again, so it isn't compiled for nothing
		var embeddedFolder = Path.GetFullPath( EmbeddedFolder ?? Path.Combine( Path.GetDirectoryName( Core ), ".source2", "borealisembedded" ) );
		if ( keptVersion != SlangCompiler.Version ) ahead.RemoveAll( a => a.Path.StartsWith( embeddedFolder, StringComparison.OrdinalIgnoreCase ) );
		if ( ahead.Count == 0 ) return prewarming = Task.CompletedTask;

		return prewarming = Task.Run( () =>
		{
			using var zone = Zones.PassModulesPrewarm.Start();
			Parallel.ForEach( ahead, new ParallelOptions { MaxDegreeOfParallelism = Math.Clamp( Environment.ProcessorCount / 4, 1, 4 ) }, a =>
			{
				try
				{
					if ( !File.Exists( a.Path ) || ModuleBeside( Path.ChangeExtension( a.Path, ".shader_c" ) ) is not { } module || !module.Has( a.Kind ) ) return;
					var task = Start( module, a.Kind, a.Values, asked: false, priority: CompilePriority.Background() );
					if ( task is null ) return;
					System.Threading.Interlocked.Increment( ref readAhead );
					task.ContinueWith( t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted );
				}
				catch ( Exception e ) when ( e is IOException or UnauthorizedAccessException or ShaderCompileException or InvalidOperationException )
				{
					// A module that's gone or doesn't compile any more: a draw that asks for it says so
				}
			} );
		} );
	}

	/// <summary>Keep a stage a draw asked for, for the next run's <see cref="Prewarm"/>.</summary>
	void Remember( string key, PassModule module, StageKind kind, Dictionary<string, int> values )
	{
		lock ( remembered )
		{
			if ( kept is null || !remembered.Add( key ) ) return;
			var entry = Entry( Path.GetFullPath( module.Path ), kind, values );
			if ( kept.TryGetValue( entry, out var last ) && last == run ) return;
			kept[entry] = run;
			if ( saving ) return;
			saving = true;
		}

		// Written a few seconds later, once, for every stage asked for meanwhile
		_ = Task.Delay( 3000 ).ContinueWith( _ => SaveStages(), TaskScheduler.Default );
	}

	/// <summary>Write the stages kept so far (a few seconds after a new one is asked for, or now, for tests).</summary>
	internal void SaveStages()
	{
		string text;
		lock ( remembered )
		{
			saving = false;
			text = WriteStages( run, kept );
		}

		try
		{
			var path = StagesPath;
			Directory.CreateDirectory( Path.GetDirectoryName( path ) );
			var temporary = $"{path}.{Environment.ProcessId}.tmp";
			File.WriteAllText( temporary, text );
			File.Move( temporary, path, overwrite: true );
		}
		catch ( Exception e ) when ( e is IOException or UnauthorizedAccessException )
		{
			// Another process is writing it: its runs' stages are kept instead
		}
	}

	static string Entry( string path, StageKind kind, Dictionary<string, int> values ) =>
		$"{path}\t{kind}\t{string.Join( ';', values.OrderBy( v => v.Key, StringComparer.Ordinal ).Select( v => $"{v.Key}={v.Value}" ) )}";

	/// <summary>
	/// The stages file's text: <c>version run</c>, then a line per stage, <c>run, module path, stage, combo values</c>
	/// tab-separated.
	/// </summary>
	internal static string WriteStages( int run, IReadOnlyDictionary<string, int> stages )
	{
		var text = new System.Text.StringBuilder( $"{SlangCompiler.Version}\t{run}\n" );
		foreach ( var (entry, last) in stages.OrderBy( s => s.Key, StringComparer.Ordinal ) ) text.Append( $"{last}\t{entry}\n" );
		return text.ToString();
	}

	/// <summary>
	/// The stages a stages file at <paramref name="path"/> keeps from the last <see cref="KeptRuns"/> runs (none without
	/// one), this run's number, the compiler version that wrote it, and what to keep from it.
	/// </summary>
	internal static List<(string Path, StageKind Kind, Dictionary<string, int> Values)> ReadStages( string path, out int run, out int version, out Dictionary<string, int> kept )
	{
		var ahead = new List<(string, StageKind, Dictionary<string, int>)>();
		kept = new Dictionary<string, int>( StringComparer.Ordinal );
		run = 0;
		version = SlangCompiler.Version;
		string[] lines;
		try
		{
			lines = File.Exists( path ) ? File.ReadAllLines( path ) : [];
		}
		catch ( Exception e ) when ( e is IOException or UnauthorizedAccessException )
		{
			lines = [];
		}

		var header = lines.Length > 0 ? lines[0].Split( '\t' ) : [];
		if ( header.Length != 2 || !int.TryParse( header[0], out version ) || !int.TryParse( header[1], out var last ) ) return ahead;
		run = last + 1;

		foreach ( var line in lines.Skip( 1 ) )
		{
			var parts = line.Split( '\t' );
			if ( parts.Length != 4 || !int.TryParse( parts[0], out var asked ) || asked < run - KeptRuns || asked > last || !Enum.TryParse<StageKind>( parts[2], out var kind ) ) continue;

			var values = new Dictionary<string, int>( StringComparer.Ordinal );
			var valid = true;
			foreach ( var pair in parts[3].Split( ';', StringSplitOptions.RemoveEmptyEntries ) )
			{
				var equals = pair.IndexOf( '=' );
				if ( equals <= 0 || !int.TryParse( pair.AsSpan( equals + 1 ), out var value ) ) valid = false;
				else values[pair[..equals]] = value;
			}

			if ( !valid ) continue;
			kept[$"{parts[1]}\t{parts[2]}\t{parts[3]}"] = asked;
			ahead.Add( (parts[1], kind, values) );
		}

		return ahead;
	}
}
