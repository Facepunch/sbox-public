using System.Collections.Concurrent;
using System.Globalization;

namespace Sandbox.ContentBuild;

/// <summary>
/// The engine's compilers for a build: a long-lived contentworker process, running every job at once on threads of its
/// own. A worker is the resource compiler with the asset system and the editor's managed compilers, so a source compiles
/// the same however its compilers are written. A job's build report makes the compiler skip associate compiles and
/// report what it read.
/// </summary>
/// <param name="executable">contentworker.</param>
/// <param name="types">The engine's native compilers (assettypes.txt).</param>
/// <param name="mounts">What the builds mount (<see cref="BuildRequest.Mounts"/>), which a worker asked about its compilers mounts too.</param>
/// <param name="compilers">The compiled tool and project assemblies the workers load (ContentWorkerAppSystem.LoadCompilers), or null.</param>
public sealed class ResourceCompiler( string executable, AssetTypeTable types, IReadOnlyList<(string Mod, string Path)> mounts, string compilers ) : IAssetCompiler
{
	public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes( 15 );

	/// <summary>
	/// Jobs a build runs at once by default: one per core and 2 GB of memory.
	/// </summary>
	public static int DefaultMaxJobs => (int)Math.Clamp( GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / (2L << 30), 1, Environment.ProcessorCount );

	/// <summary>
	/// Workers get a render device. Without one, as on a build machine with no GPU, they have the empty render system:
	/// native compiles, the built-in texture generators and reads of other textures' sizes and formats work the same. A
	/// generator that makes no image of its own (copying a compiled .vtex) fails, saying it needs a device; a project's
	/// compiler that renders isn't supported without one.
	/// </summary>
	public bool RenderDevice { get; init; }

	/// <summary>
	/// Runs each job in this process, which has the same compilers loaded (the editor), rather than in a worker: mode,
	/// source, staging, report and compiler arguments to whether it succeeded, as a worker answers. Null starts workers.
	/// </summary>
	public Func<string, string, string, string, IReadOnlyList<string>, bool> InProcess { get; init; }

	/// <summary>
	/// Worker processes started.
	/// </summary>
	public int Launches => launches;

	/// <summary>
	/// Why each worker that went before the end went: it crashed, timed out, was cancelled, or couldn't guarantee its
	/// next job starts clean.
	/// </summary>
	public IReadOnlyCollection<string> Ended => ended;

	/// <summary>
	/// Idle workers stopped for a worker with other mounts.
	/// </summary>
	public int Evicted => evicted;

	readonly object gate = new();
	readonly List<CompilerWorker> workers = [];
	readonly ConcurrentQueue<string> ended = new();
	readonly SemaphoreSlim isolated = new( 1 );
	int launches, evicted;
	Dictionary<string, string> managed;

	/// <summary>
	/// The processes of the workers running no job (for tests).
	/// </summary>
	internal IReadOnlyList<int> IdleProcessIds { get { lock ( gate ) return workers.Where( x => x.Busy == 0 ).Select( x => x.ProcessId ).ToList(); } }

	/// <summary>
	/// A native compiler's type is classified by what that compiler reads. Managed code that takes part in compiling a
	/// type (a game resource, a compiler registered for its extension) can read compiled outputs through its context,
	/// so that type is described first. Types this build leaves to other tools (shaders, maps) stay out.
	/// </summary>
	public AssetKind Classify( string sourceExtension, out string outputExtension )
	{
		var kind = types.Classify( sourceExtension, out outputExtension );
		if ( kind == AssetKind.None && types.Contains( sourceExtension ) ) return kind;
		if ( !Managed().TryGetValue( sourceExtension, out var output ) ) return kind;

		outputExtension ??= output.Length > 0 ? output : null;
		return outputExtension is null ? kind : AssetKind.Described;
	}

	/// <summary>
	/// The source types the workers' managed compilers take part in, with a game resource's output extension, asked once
	/// of a worker.
	/// </summary>
	Dictionary<string, string> Managed()
	{
		lock ( gate )
		{
			if ( managed is not null ) return managed;
		}

		var lines = Ask( "types", null );
		var table = new Dictionary<string, string>( StringComparer.OrdinalIgnoreCase );
		foreach ( var fields in lines.Select( x => x.Split( '\t' ) ).Where( x => x.Length == 2 ) )
			table[fields[0]] = fields[1];

		lock ( gate ) return managed ??= table;
	}

	public IReadOnlyList<uint?> Fingerprints( IReadOnlyList<SpecialDependency> dependencies )
	{
		var lines = Ask( "fingerprints", dependencies.Select( x => $"{x.Compiler}\t{x.Data}\t{x.Name}" ) );
		if ( lines.Length != dependencies.Count )
			throw new InvalidOperationException( $"contentworker fingerprints returned {lines.Length} of {dependencies.Count}" );

		return lines.Select( x => uint.TryParse( x, NumberStyles.HexNumber, null, out var v ) ? v : (uint?)null ).ToArray();
	}

	/// <summary>
	/// Each worker's next job checks it has no older one of these loaded first: one that has refuses it and takes no
	/// more, as a job would load that (see ContentCompilerHost.Admit). The jobs it runs started before they were
	/// published, so the build reruns any that read them.
	/// </summary>
	public void Published( IReadOnlyList<string> paths )
	{
		// The editor reloads what it has loaded as it's published
		if ( InProcess is not null ) return;

		lock ( gate )
		{
			foreach ( var worker in workers ) worker.Published( paths );
		}
	}

	/// <summary>
	/// A question about the compilers rather than a compile, answered as a job (by a worker that then takes compiles):
	/// what it writes to a file.
	/// </summary>
	string[] Ask( string mode, IEnumerable<string> input )
	{
		var dir = Directory.CreateTempSubdirectory( "sbox-compilers" ).FullName;
		try
		{
			var source = Path.Combine( dir, "in.txt" );
			var answer = Path.Combine( dir, "out.txt" );
			File.WriteAllLines( source, input ?? [] );

			var reply = Run( mounts, [], mode, source, Directory.CreateDirectory( Path.Combine( dir, "staging" ) ).FullName, answer, default );
			if ( reply?.Outcome != CompilerWorker.Outcome.Succeeded ) throw new InvalidOperationException( $"contentworker couldn't answer {mode}: {reply?.Reason}\n{reply?.Log}" );
			return File.ReadAllLines( answer );
		}
		finally
		{
			Directory.Delete( dir, true );
		}
	}

	public CompileResult Compile( CompileJob job, CancellationToken token )
	{
		var report = Path.Combine( job.Staging, "report.txt" );
		var mode = job.Mode switch { JobMode.Describe => "describe", JobMode.Adopt => "adopt", _ => "compile" };

		var answer = Run( job.Mounts, job.CompilerArgs, mode, job.Source, job.Staging, report, token );
		if ( answer is null || token.IsCancellationRequested ) return Failed( "cancelled" );
		if ( answer.Outcome is not (CompilerWorker.Outcome.Succeeded or CompilerWorker.Outcome.Failed) ) return Failed( $"contentworker {answer.Reason}\n{answer.Log}" );

		// What the job logged on its thread
		var log = $"{report}.log";
		var text = File.Exists( log ) ? File.ReadAllText( log ).Trim() : "";
		File.Delete( log );

		var lines = File.Exists( report ) ? File.ReadAllLines( report ) : [];
		File.Delete( report );
		return BuildReportParser.Parse( lines, answer.Outcome == CompilerWorker.Outcome.Succeeded, text );
	}

	/// <summary>
	/// Runs a job on the worker for these mounts, or null if cancelled first. A worker that dies before answering is
	/// replaced and the job runs again from empty staging, up to twice. Its jobs died with it, so any of them may have
	/// been the cause: a job lost twice runs its last time on a worker of its own, one such job at a time, so a job that
	/// crashes every worker fails alone. A third loss, a timeout or a worker that never got ready (it says why: a render
	/// device it couldn't create, compilers that didn't load) is the answer. A job a worker refuses (it has an older
	/// output loaded) runs on a new one, as does one interrupted by another job's timeout or cancellation; neither counts
	/// as a loss.
	/// </summary>
	CompilerWorker.Answer Run( IReadOnlyList<(string Mod, string Path)> mounted, IReadOnlyList<string> compilerArgs, string mode, string source, string staging, string report, CancellationToken token )
	{
		if ( InProcess is not null )
			return token.IsCancellationRequested ? null : new( InProcess( mode, source, staging, report, compilerArgs ) ? CompilerWorker.Outcome.Succeeded : CompilerWorker.Outcome.Failed, null, "" );

		for ( var attempt = 0; ; attempt++ )
		{
			var alone = attempt == 2;
			if ( alone )
			{
				try { isolated.Wait( token ); }
				catch ( OperationCanceledException ) { return null; }
			}

			CompilerWorker worker;
			CompilerWorker.Answer answer;
			try
			{
				worker = Take( mounted, alone, token );
				if ( worker is null ) return null;
				try
				{
					answer = worker.Run( mode, source, staging, report, compilerArgs, Timeout, token );
				}
				catch
				{
					Release( worker, true, "failed" );
					throw;
				}

				Release( worker, answer.Outcome is not (CompilerWorker.Outcome.Succeeded or CompilerWorker.Outcome.Failed), answer.Reason );
			}
			finally
			{
				if ( alone ) isolated.Release();
			}

			// A refused job never ran, and an interrupted one isn't to blame: neither counts as an attempt. A worker that
			// never got ready is the answer, as it would be again.
			if ( answer.Outcome == CompilerWorker.Outcome.Refused || (answer.Outcome == CompilerWorker.Outcome.Interrupted && worker.Started && !token.IsCancellationRequested) )
			{
				if ( answer.Outcome == CompilerWorker.Outcome.Interrupted ) Clear( staging );
				attempt--;
				continue;
			}

			if ( answer.Outcome != CompilerWorker.Outcome.Lost || token.IsCancellationRequested || alone || !worker.Started ) return answer;

			Clear( staging );
		}
	}

	static void Clear( string directory )
	{
		Directory.Delete( directory, true );
		Directory.CreateDirectory( directory );
	}

	static CompileResult Failed( string log ) => new( false, log, [], [], [], [], [] );

	/// <summary>
	/// The worker for these mounts, which runs all their jobs, or a new one; or the one for a job run <paramref name="alone"/>.
	/// Idle workers of other mounts go: each holds ~1.3 GB before it compiles anything.
	/// </summary>
	CompilerWorker Take( IReadOnlyList<(string Mod, string Path)> mounted, bool alone, CancellationToken token )
	{
		if ( token.IsCancellationRequested ) return null;

		// Any root's job: a compile takes its mod from the source's path. Each mount is added in front of the last,
		// as BuildRequest.Mounts orders them.
		var key = string.Join( ";", mounted.Select( x => $"{x.Mod}|{x.Path}" ) );
		var args = new List<string>
		{
			// Pool threads for jobs' own parallel work (texture compression), which a lone heavy compile (a skybox
			// prefilter) has all of. Half the logical processors: the jobs' own threads fill the rest, and more only
			// switches between them (~4% slower with as many as there are processors)
			"-testmode", "-ignorecontentasserts", "-unbufferedio", "-threads", $"{Math.Max( 1, Environment.ProcessorCount / 2 )}",
			"-searchpaths", key,
		};

		// A GPU, or a CPU device when that's all there is. The pipeline cache would be one file every worker writes.
		if ( RenderDevice ) args.AddRange( ["-vulkan", "-vulkan_allow_cpu", "-vulkan_disable_pipeline_cache"] );
		if ( compilers is not null ) args.AddRange( ["-compilers", compilers] );

		CompilerWorker worker;
		List<CompilerWorker> idle;
		lock ( gate )
		{
			var pool = alone ? $"alone {key}" : key;
			worker = workers.FirstOrDefault( x => x.Key == pool );
			if ( worker is not null )
			{
				worker.Busy++;
				return worker;
			}

			// Started here, so the jobs asking at the same time find it. An output root nothing was written to yet is
			// mounted all the same.
			foreach ( var (_, path) in mounted ) Directory.CreateDirectory( path );
			worker = new CompilerWorker( executable, pool, args, Path.GetDirectoryName( executable ) ) { Busy = 1 };
			Interlocked.Increment( ref launches );

			idle = workers.Where( x => x.Busy == 0 ).ToList();
			workers.RemoveAll( idle.Contains );
			workers.Add( worker );
		}

		Interlocked.Add( ref evicted, idle.Count );
		foreach ( var old in idle ) old.Stop();
		return worker;
	}

	/// <summary>
	/// A job is done with its worker. One that <paramref name="leaves"/> takes no more jobs: it crashed, timed out, was
	/// cancelled, or couldn't guarantee its next job starts clean. The jobs it ran besides come back, answered or not,
	/// and the last one stops it.
	/// </summary>
	void Release( CompilerWorker worker, bool leaves, string reason )
	{
		bool stop;
		lock ( gate )
		{
			if ( leaves && workers.Remove( worker ) ) ended.Enqueue( reason ?? "ended" );
			stop = --worker.Busy == 0 && !workers.Contains( worker );
		}
		if ( stop ) worker.Stop();
	}

	/// <summary>
	/// Every worker goes (a build has none busy once it returns), all at once: each takes seconds to shut its engine
	/// down. The compiler can be used again afterwards and starts new ones.
	/// </summary>
	public void Finished()
	{
		List<CompilerWorker> all;
		lock ( gate )
		{
			all = [.. workers];
			workers.Clear();
		}
		Parallel.ForEach( all, worker => worker.Stop() );
	}
}
