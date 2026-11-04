using System.Collections.Concurrent;
using System.Globalization;

namespace Sandbox.ContentBuild;

/// <summary>
/// The engine's compilers for a build: a few long-lived contentworker processes, each taking one job at a time. A
/// worker is the resource compiler with the asset system and the editor's managed compilers, so a source compiles the
/// same however its compilers are written. A job's build report makes the compiler skip associate compiles and report
/// what it read.
/// </summary>
/// <param name="executable">contentworker.</param>
/// <param name="types">The engine's native compilers (assettypes.txt).</param>
/// <param name="mounts">What the builds mount (<see cref="BuildRequest.Mounts"/>), which a worker asked about its compilers mounts too.</param>
/// <param name="compilers">The compiled tool and project assemblies the workers load (ContentWorkerAppSystem.LoadCompilers), or null.</param>
public sealed class ResourceCompiler( string executable, AssetTypeTable types, IReadOnlyList<(string Mod, string Path)> mounts, string compilers ) : IAssetCompiler
{
	public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes( 15 );

	/// <summary>
	/// Worker processes at most, over all roots; at least as many as the build runs jobs at once. A worker holds ~1.3 GB
	/// before it compiles anything, so by default one per core and 4 GB of memory.
	/// </summary>
	public int MaxWorkers { get; init; } = DefaultMaxWorkers;

	/// <summary>
	/// Shared default for the worker pool and the command-line build's job limit.
	/// </summary>
	public static int DefaultMaxWorkers => (int)Math.Clamp( GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / (4L << 30), 1, Environment.ProcessorCount );

	/// <summary>
	/// Workers get a render device. Without one, as on a build machine with no GPU, they have the empty render system:
	/// native compiles, the built-in texture generators and reads of other textures' sizes and formats work the same. A
	/// generator that makes no image of its own (copying a compiled .vtex) fails, saying it needs a device; a project's
	/// compiler that renders isn't supported without one.
	/// </summary>
	public bool RenderDevice { get; init; }

	/// <summary>
	/// Answers the questions about the compilers a worker would (see Ask) in this process, which has the same compilers
	/// loaded (the editor), so asking needs no worker. Null asks a worker.
	/// </summary>
	public Func<string, IEnumerable<string>, IReadOnlyList<string>> Answer { get; init; }

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
	/// Idle workers stopped to make room for a worker with other mounts or arguments.
	/// </summary>
	public int Evicted => evicted;

	readonly object gate = new();
	readonly List<CompilerWorker> idle = [];
	readonly ConcurrentQueue<string> ended = new();
	SemaphoreSlim slots;
	int launches, evicted;
	Dictionary<string, string> managed;

	/// <summary>
	/// The processes of the workers waiting for a job (for tests).
	/// </summary>
	internal IReadOnlyList<int> IdleProcessIds { get { lock ( gate ) return idle.Select( x => x.ProcessId ).ToList(); } }

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
	/// A question about the compilers rather than a compile, answered by a worker that then takes compiles: what it
	/// writes to a file.
	/// </summary>
	string[] Ask( string mode, IEnumerable<string> input )
	{
		if ( Answer is not null ) return Answer( mode, input ?? [] ).ToArray();

		var dir = Directory.CreateTempSubdirectory( "sbox-compilers" ).FullName;
		try
		{
			var source = Path.Combine( dir, "in.txt" );
			var answer = Path.Combine( dir, "out.txt" );
			File.WriteAllLines( source, input ?? [] );

			var reply = Run( mounts, [], mode, source, Directory.CreateDirectory( Path.Combine( dir, "staging" ) ).FullName, answer, default );
			if ( reply?.Answered != true || !reply.Result ) throw new InvalidOperationException( $"contentworker couldn't answer {mode}: {reply?.Reason}\n{reply?.Log}" );
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
		if ( !answer.Answered ) return Failed( $"contentworker {answer.Reason}\n{answer.Log}" );

		var lines = File.Exists( report ) ? File.ReadAllLines( report ) : [];
		File.Delete( report );
		return BuildReportParser.Parse( lines, answer.Result, answer.Log );
	}

	/// <summary>
	/// Runs a job on a worker for these mounts and arguments, or null if cancelled first. A worker that dies before
	/// answering is replaced and the job runs once more on the new one, from empty staging; a second loss, a timeout or
	/// a worker that never got ready (it says why: a render device it couldn't create, compilers that didn't load) is the answer.
	/// </summary>
	CompilerWorker.Answer Run( IReadOnlyList<(string Mod, string Path)> mounted, IReadOnlyList<string> compilerArgs, string mode, string source, string staging, string report, CancellationToken token )
	{
		for ( var attempt = 0; ; attempt++ )
		{
			CompilerWorker.Answer answer;
			var worker = Take( mounted, compilerArgs, token );
			if ( worker is null ) return null;
			try
			{
				answer = worker.Run( mode, source, staging, report, Timeout, token );
			}
			catch
			{
				End( worker, "failed" );
				throw;
			}

			if ( answer.Answered && answer.TakesAnother ) Return( worker );
			else End( worker, answer.Reason );

			if ( answer.Answered || token.IsCancellationRequested || attempt > 0 || !worker.Started || answer.Reason.StartsWith( "timed out" ) ) return answer;

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
	/// An idle worker for these mounts and arguments, or a new one. When the cap is reached by idle workers of
	/// other mounts, one of them goes; the cap holds because a slot is taken for every worker in use.
	/// </summary>
	CompilerWorker Take( IReadOnlyList<(string Mod, string Path)> mounted, IReadOnlyList<string> compilerArgs, CancellationToken token )
	{
		lock ( gate ) slots ??= new SemaphoreSlim( MaxWorkers );
		try { slots.Wait( token ); }
		catch ( OperationCanceledException ) { return null; }

		// Any root's job: a compile takes its mod from the source's path. Each mount is added in front of the last,
		// as BuildRequest.Mounts orders them.
		var searchPaths = string.Join( ";", mounted.Select( x => $"{x.Mod}|{x.Path}" ) );
		var key = string.Join( '\n', [searchPaths, .. compilerArgs] );
		CompilerWorker evict = null;
		lock ( gate )
		{
			var found = idle.FirstOrDefault( x => x.Key == key );
			if ( found is not null )
			{
				idle.Remove( found );
				return found;
			}

			// Workers in use (this one included) plus idle ones stay within the cap
			if ( idle.Count > slots.CurrentCount )
			{
				evict = idle[0];
				idle.RemoveAt( 0 );
			}
		}
		Interlocked.Increment( ref launches );
		var args = new List<string>
		{
			// A few threads each: a lone heavy compile (a skybox prefilter) finishes ~2.5x sooner, and a machine
			// already full of compiles loses nothing to the extra threads
			"-testmode", "-ignorecontentasserts", "-unbufferedio", "-threads", "4",
			"-searchpaths", searchPaths,
		};

		// A GPU, or a CPU device when that's all there is. The pipeline cache would be one file every worker writes.
		if ( RenderDevice ) args.AddRange( ["-vulkan", "-vulkan_allow_cpu", "-vulkan_disable_pipeline_cache"] );
		if ( compilers is not null ) args.AddRange( ["-compilers", compilers] );
		args.AddRange( compilerArgs );

		try
		{
			// An output root nothing was written to yet is mounted all the same
			foreach ( var (_, path) in mounted ) Directory.CreateDirectory( path );

			if ( evict is not null )
			{
				Interlocked.Increment( ref evicted );
				evict.Stop();
			}
			return new CompilerWorker( executable, key, args, Path.GetDirectoryName( executable ) );
		}
		catch
		{
			slots.Release();
			throw;
		}
	}

	void Return( CompilerWorker worker )
	{
		lock ( gate ) idle.Add( worker );
		slots.Release();
	}

	/// <summary>
	/// A worker leaves early; its slot is free again however stopping it goes.
	/// </summary>
	void End( CompilerWorker worker, string reason )
	{
		ended.Enqueue( reason ?? "ended" );
		try { worker.Stop(); }
		finally { slots.Release(); }
	}

	/// <summary>
	/// Every idle worker goes (a build has none busy once it returns), all at once: each takes seconds to shut its
	/// engine down. The compiler can be used again afterwards and starts new ones.
	/// </summary>
	public void Finished()
	{
		List<CompilerWorker> all;
		lock ( gate )
		{
			all = [.. idle];
			idle.Clear();
		}
		Parallel.ForEach( all, worker => worker.Stop() );
	}
}
