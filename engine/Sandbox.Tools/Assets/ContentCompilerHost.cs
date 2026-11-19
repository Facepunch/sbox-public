using NativeEngine;
using Sandbox.Diagnostics;
using Sandbox.Engine;
using Sandbox.Tasks;
using System;
using System.Collections.Concurrent;
using System.Reflection;
using System.Text;
using System.Threading;

namespace Editor;

/// <summary>
/// The editor's resource compilers in a content build worker: game resources and managed compilers compile there
/// the way they do in the editor, through the same type libraries and compile context, without its windows or its
/// asset registry (a compile's references are found in the mounts, see ResourceCompileContextImp.ScanJson).
/// </summary>
internal static class ContentCompilerHost
{
	/// <summary>
	/// Before the native asset system loads: the resource compiler reaches managed compilers through it.
	/// </summary>
	internal static void InitializeInterop()
	{
		Managed.SourceAssetSytem.NativeInterop.Initialize();

		// The tools' game data classes, which a model's game data compiles with
		Managed.SourceTools.NativeInterop.Initialize();
	}

	/// <summary>
	/// The type libraries with the compiled tool and project assemblies the build's compilers and game resource types
	/// come from, and the mounts compiles read.
	/// </summary>
	internal static void InitializeCompilers( IReadOnlyList<Assembly> assemblies, IEnumerable<(string Mod, string Path)> mounts )
	{
		// The build's roots are the content, where the editor has its project's. Generated resources (an embedded
		// texture) go to the build's "transient" output root, mounted under the roots, where the editor has its project's
		// transient folder. Everything is mounted, as the editor mounts it for a compile to read: the engine's core
		// content first, under everything else, as the resource compiler has it.
		var content = new AggregateFileSystem();
		var mounted = new AggregateFileSystem();
		content.Mount( EngineFileSystem.CoreContent );
		mounted.Mount( EngineFileSystem.CoreContent );
		foreach ( var (mod, path) in mounts )
		{
			var folder = new LocalFileSystem( path );
			if ( mod == "transient" ) FileSystem.Transient = folder;
			else content.Mount( folder );
			mounted.Mount( folder );
		}
		FileSystem.Content = content;
		GlobalContext.Current.FileMount = mounted;

		ToolsDll.CreateEditorTypeLibrary();

		// Compiled resources a compile loads are made with the game's type library, as in the editor
		Game.TypeLibrary = new Sandbox.Internal.TypeLibrary();
		Game.TypeLibrary.AddIntrinsicTypes();
		Game.TypeLibrary.AddAssembly( typeof( Vector3 ).Assembly, false );
		Game.TypeLibrary.AddAssembly( typeof( EngineLoop ).Assembly, false );
		Game.TypeLibrary.AddAssembly( typeof( Facepunch.ActionGraphs.ActionGraph ).Assembly, false );

		foreach ( var assembly in assemblies )
		{
			EditorTypeLibrary.AddAssembly( assembly, true );
			Game.TypeLibrary.AddAssembly( assembly, true );
		}

		// A model's game data compiles with the classes the editor has, the tools' and the engine's first
		GameData.InitializeForCompiles( IResourceCompilerSystem.ModelGameData(), [typeof( ContentCompilerHost ).Assembly, typeof( Model ).Assembly, .. assemblies] );
	}

	/// <summary>
	/// Says it's ready, then answers jobs from standard input until it ends, one per line:
	/// <c>id \t compile|describe|adopt|fingerprints|types \t source \t staging \t report \t count [\t published]... [\t compiler argument]...</c>,
	/// where published are the count outputs published since the last request (see <see cref="Admit"/>). Jobs run at once
	/// (<see cref="RunJob"/>) while the main thread does what they need of it; whenever none runs, it lets go of what
	/// they loaded. Once a job's report and its log (<c>report.log</c>) are written, one line answers it:
	/// <c>\x01done \t id \t succeeded|failed|refused|error [\t why]</c>: refused, a job it won't run (see <see cref="Admit"/>);
	/// error, one it couldn't. After either it takes no more.
	/// </summary>
	internal static int Serve()
	{
		unloadWhenIdle = true;
		var output = new object();
		void Reply( string id, string result )
		{
			lock ( output ) System.Console.Out.Write( $"\u0001done\t{id}\t{result}\n" );
		}

		var answering = 0;
		var ended = false;
		var exitCode = -1;
		var reader = new Thread( () =>
		{
			while ( System.Console.In.ReadLine() is { } line )
			{
				var fields = line.Split( '\t' );
				if ( fields.Length < 6 || !int.TryParse( fields[5], out var count ) || count < 0 || fields.Length < 6 + count )
				{
					Reply( "?", "error\tmalformed request" );
					Volatile.Write( ref exitCode, 1 );
					return;
				}

				// Here, in the order they came, so a job finds every output published before it was asked for
				lock ( published ) published.UnionWith( fields[6..(6 + count)] );

				// A thread of its own, as it waits for its job: the pool's would run out
				Interlocked.Increment( ref answering );
				new Thread( () =>
				{
					try
					{
						if ( !RunOnMainThread( null, Admit ) )
						{
							Reply( fields[0], "refused\tit has an older output loaded" );
							return;
						}

						Reply( fields[0], RunJob( fields[1], fields[2], fields[3], fields[4], fields[(6 + count)..] ) ? "succeeded" : "failed" );
					}
					// The pool sends no more, and the jobs running beside it finish
					catch ( Exception e )
					{
						Reply( fields[0], $"error\t{e.Message.ReplaceLineEndings( " " )}" );
					}
					finally { Interlocked.Decrement( ref answering ); }
				} )
				{ IsBackground = true, Name = $"contentworker request {fields[0]}" }.Start();
			}
			Volatile.Write( ref ended, true );
		} )
		{ IsBackground = true, Name = "contentworker requests" };
		reader.Start();

		System.Console.Out.Write( "\u0001ready\n" );
		while ( Volatile.Read( ref exitCode ) < 0 && !(Volatile.Read( ref ended ) && Volatile.Read( ref answering ) == 0) )
		{
			Pump( MainThreadWorkPoll );
			Logging.PushQueuedMessages();
		}
		return Math.Max( exitCode, 0 );
	}

	/// <summary>
	/// Runs a build job in this process and waits for it: on a thread of its own, while others run, its staging mounted
	/// for it on the main thread from before it starts until after it ends (see <see cref="Pump"/>). A worker lets go of
	/// what jobs loaded whenever none runs. A question (<see cref="Answer"/>) is answered on the main thread, as a job of
	/// its own so it reads the files as they are now.
	/// </summary>
	internal static bool RunJob( string mode, string source, string staging, string report, IReadOnlyList<string> compilerArgs )
	{
		if ( mode is "types" or "fingerprints" )
		{
			RunOnMainThread( staging, () => System.IO.File.WriteAllLines( report, Answer( mode, System.IO.File.ReadAllLines( source ) ) ) );
			return true;
		}

		Logging.Capture ??= JobLog;
		PackageLoader.HoldHotload ??= () => !compiles.IsEmpty;
		RunOnMainThread( null, () =>
		{
			IResourceCompilerSystem.MountBuildJobStaging( staging, true );
			running++;
		} );

		var job = jobs[staging] = new BuildJob( staging );
		var result = false;
		try
		{
			var args = string.Join( '\t', compilerArgs );
			var thread = new Thread( () => result = IResourceCompilerSystem.RunBuildJob( mode, source, staging, report, args ), JobStackSize )
			{
				IsBackground = true,
				Name = $"build job {System.IO.Path.GetFileName( source )}",
			};
			thread.Start();
			while ( !thread.Join( ThreadSafe.IsMainThread ? 0 : Timeout.Infinite ) ) Pump( MainThreadWorkPoll );
			System.IO.File.WriteAllText( $"{report}.log", job.Log.ToString() );
		}
		finally
		{
			jobs.TryRemove( staging, out _ );
			RunOnMainThread( null, () =>
			{
				IResourceCompilerSystem.MountBuildJobStaging( staging, false );
				if ( --running == 0 && unloadWhenIdle ) IResourceCompilerSystem.UnloadBuildJobs();
			} );
		}
		return result;
	}

	/// <summary>
	/// Does what jobs in this process wait for the main thread to do: mount and unmount their staging, run a managed
	/// compile, load a resource. On the main thread, whenever it can, waiting up to <paramref name="wait"/> milliseconds
	/// for work if there's none.
	/// </summary>
	internal static void Pump( int wait = 0 )
	{
		// Not from within work it runs (a managed compile that spins the editor): that runs as its job until it returns
		if ( pumping ) return;
		pumping = true;
		try
		{
			IResourceCompilerSystem.RunMainThreadWork();

			// Only what's queued now: a compile that keeps awaiting lets the frame run between its steps
			var queued = mainThread.Count;
			if ( !mainThread.TryTake( out var work, wait ) ) return;
			work();
			while ( --queued > 0 && mainThread.TryTake( out work ) ) work();
		}
		finally { pumping = false; }
	}

	static bool pumping;

	/// <summary>
	/// On the main thread, inside work <see cref="Pump"/> runs: jobs get nothing more from it until that returns.
	/// </summary>
	internal static bool IsPumping => pumping;

	/// <summary>
	/// On the main thread, before a job starts: whether this worker may run it. One that has loaded an older one of the
	/// outputs published since (or is loading it) never runs another, as a job would get that, and the job runs on a new
	/// worker. A job asked for after an output was published finds it here (<see cref="Serve"/>); one already running
	/// started before, so the build reruns it if it read it.
	/// </summary>
	static bool Admit()
	{
		string[] outputs;
		lock ( published )
		{
			outputs = [.. published];
			published.Clear();
		}
		retired |= outputs.Any( IResourceCompilerSystem.HasLoadedCompiledFile );
		return !retired;
	}

	/// <summary>
	/// Outputs published since the last job started (<see cref="Admit"/>).
	/// </summary>
	static readonly HashSet<string> published = [];

	/// <summary>
	/// Has an older output loaded than what's published: it takes no more jobs. Main thread only.
	/// </summary>
	static bool retired;

	/// <summary>
	/// Awaited on the main thread, until no job runs in this process. None starts before the main thread does their work
	/// again, so what follows can change what jobs read (the tools' game data) without one reading it.
	/// </summary>
	internal static async Task NoJobsRunning()
	{
		while ( running > 0 ) await Task.Yield();
	}

	/// <summary>
	/// On the main thread, until no job runs in this process: before it shuts down what they use. A job whose managed
	/// compile is waiting for something is abandoned rather than waited for: once what it awaited is done (work it runs
	/// elsewhere, a native encode), the compile goes no further, and the job waits for it until the process ends, so
	/// nothing it uses is freed. What's logged afterwards is nobody's.
	/// </summary>
	internal static void WaitForJobs()
	{
		if ( pumping ) return;
		closed = true;
		var closing = System.Diagnostics.Stopwatch.StartNew();
		while ( running > compiles.Keys.Select( x => x.Job ).Distinct().Count() || Volatile.Read( ref childCompiles ) > 0 || (compiles.Keys.Any( x => x.Away ) && closing.Elapsed < AbandonWait) ) Pump( MainThreadWorkPoll );
		Logging.Capture = null;
	}

	/// <summary>
	/// How long closing waits for an abandoned compile's awaited work, which may never end (a frame that won't come).
	/// </summary>
	static readonly TimeSpan AbandonWait = TimeSpan.FromSeconds( 10 );

	static volatile bool closed;

	/// <summary>
	/// Runs a managed compile on the main thread, where resources load and the type library changes, while the build job
	/// it's for waits: native compiles run on the jobs' threads at once, managed ones on the main thread. It runs as the
	/// job (<see cref="ThreadJob"/>): what it logs and sees (its staging) is the job's. While it awaits something the main thread
	/// is free (the editor ticks), and no hotload happens until it ends. The job's thread meanwhile compiles the children
	/// it awaits (embedded resources, <see cref="BuildJob.CompileChild"/>). Off a job's thread it runs here, pumping only
	/// its own continuations. It mustn't await work another job's compile started, which may need that compile to go on.
	/// Once closing, a job's compile fails rather than start: the native work asking for it can end.
	/// </summary>
	internal static bool CompileOnMainThread( Func<Task<bool>> compile )
	{
		if ( ThreadSafe.IsMainThread || CurrentJob is not { } job ) return AsyncPump.RunSynchronously( compile );
		if ( closed ) return false;

		// A child compiling on this thread waits here for its own managed compile: closing doesn't wait for it meanwhile
		// (it's abandoned with the compile), and it counts again, on the main thread, before it's woken
		var parked = job.Children > 0;
		if ( parked ) Interlocked.Decrement( ref childCompiles );

		var done = new TaskCompletionSource<bool>( TaskCreationOptions.RunContinuationsAsynchronously );
		var context = new JobContext( job );
		compiles[context] = true;
		context.Post( async _ =>
		{
			try { done.SetResult( await compile() ); }
			catch ( Exception e ) { done.SetException( e ); }
			finally
			{
				compiles.TryRemove( context, out var _ );
				if ( parked ) Interlocked.Increment( ref childCompiles );
				job.Work.Add( static () => { } );
			}
		}, null );

		while ( !done.Task.IsCompleted ) job.Work.Take()();
		return done.Task.GetAwaiter().GetResult();
	}

	/// <summary>
	/// Compiles a child (a native compile: an embedded resource), on the job's own thread if this is a build job's managed
	/// compile (<see cref="BuildJob.CompileChild"/>), so it's awaited rather than block the main thread; right away otherwise.
	/// </summary>
	internal static Task<bool> CompileChild( Func<bool> compile ) => SynchronizationContext.Current is JobContext context ? context.Job.CompileChild( compile ) : Task.FromResult( compile() );

	/// <summary>
	/// A build job running in this process: its log (what a thread logs while it runs the job, or does main-thread work
	/// for it), and what its thread does for its managed compiles while it waits for them (<see cref="CompileOnMainThread"/>).
	/// </summary>
	sealed class BuildJob( string name )
	{
		public readonly string Name = name;
		public readonly StringBuilder Log = new();
		public readonly BlockingCollection<Action> Work = new();

		/// <summary>
		/// Children compiling on its thread (nested in each other's managed compiles). Its thread only.
		/// </summary>
		public int Children;

		/// <summary>
		/// Compiles a child (a native compile: an embedded resource) on the job's own thread, in its scope, as the job's
		/// other native work is: its managed compile awaits it there rather than block the main thread, one at a time.
		/// </summary>
		public Task<bool> CompileChild( Func<bool> compile )
		{
			var compiled = new TaskCompletionSource<bool>( TaskCreationOptions.RunContinuationsAsynchronously );
			Interlocked.Increment( ref childCompiles );
			Work.Add( () =>
			{
				Children++;
				try { compiled.SetResult( compile() ); }
				catch ( Exception e ) { compiled.SetException( e ); }
				finally
				{
					Children--;
					Interlocked.Decrement( ref childCompiles );
				}
			} );
			return compiled.Task;
		}
	}

	/// <summary>
	/// The jobs running, by name (their staging).
	/// </summary>
	static readonly ConcurrentDictionary<string, BuildJob> jobs = new();

	/// <summary>
	/// The job this thread runs or does main-thread work for, as tier0 has it.
	/// </summary>
	static BuildJob CurrentJob => ThreadJob.Name() is { } name && jobs.TryGetValue( name, out var job ) ? job : null;

	/// <summary>
	/// Children compiling on jobs' threads for managed compiles: closing waits for them, as they run native code.
	/// </summary>
	static int childCompiles;

	/// <summary>
	/// Build jobs' managed compiles on the main thread, from when they're asked for until they end.
	/// </summary>
	static readonly ConcurrentDictionary<JobContext, bool> compiles = new();

	/// <summary>
	/// Where a build job's managed compile carries on after what it awaits: on the main thread, as the job.
	/// </summary>
	sealed class JobContext( BuildJob job ) : SynchronizationContext
	{
		internal BuildJob Job => job;

		/// <summary>
		/// Awaiting something that isn't done yet: it ran, and nothing it awaited has posted it back since.
		/// </summary>
		internal volatile bool Away;

		public override void Post( SendOrPostCallback callback, object state )
		{
			Away = false;
			mainThread.Add( () =>
			{
				if ( closed ) return;

				var previous = Current;
				SetSynchronizationContext( this );
				ThreadJob.Begin( job.Name );
				Away = true;
				try { callback( state ); }
				finally
				{
					ThreadJob.End();
					SetSynchronizationContext( previous );
				}
			} );
		}

		public override void Send( SendOrPostCallback callback, object state ) => throw new NotSupportedException( "A build job's compile only posts to the main thread" );

		public override SynchronizationContext CreateCopy() => this;
	}

	/// <summary>
	/// Runs work on the main thread as the build job this thread runs, while it waits (the type library a managed compile
	/// is chosen from changes there), or right here off a job's thread.
	/// </summary>
	internal static T OnMainThread<T>( Func<T> work ) => ThreadSafe.IsMainThread || CurrentJob is not { } job ? work() : RunOnMainThread( job.Name, work );

	/// <summary>
	/// Runs work on the main thread as the job named, if any, and waits for it (<see cref="Pump"/> runs it), or right
	/// away on the main thread.
	/// </summary>
	static T RunOnMainThread<T>( string job, Func<T> work )
	{
		T AsJob()
		{
			if ( job is null ) return work();
			ThreadJob.Begin( job );
			try { return work(); }
			finally { ThreadJob.End(); }
		}

		if ( ThreadSafe.IsMainThread ) return AsJob();

		T result = default;
		System.Runtime.ExceptionServices.ExceptionDispatchInfo error = null;
		using var done = new ManualResetEventSlim();
		mainThread.Add( () =>
		{
			try { result = AsJob(); }
			catch ( Exception e ) { error = System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture( e ); }
			finally { done.Set(); }
		} );
		done.Wait();
		error?.Throw();
		return result;
	}

	static void RunOnMainThread( string job, Action work ) => RunOnMainThread( job, () => { work(); return 0; } );

	/// <summary>
	/// What jobs wait for the main thread to do (<see cref="RunOnMainThread"/>), in the order they asked.
	/// </summary>
	static readonly BlockingCollection<Action> mainThread = new();

	/// <summary>
	/// Jobs in this process with their staging mounted. Main thread only.
	/// </summary>
	static int running;

	/// <summary>
	/// Whether what jobs loaded is let go of whenever none runs: in a worker, not in the editor, whose resources are its
	/// own.
	/// </summary>
	static bool unloadWhenIdle;

	static StringBuilder JobLog() => CurrentJob?.Log;

	/// <summary>
	/// A compile's thread gets the stack the main thread has (compilers recurse deeply through a model's nodes).
	/// </summary>
	const int JobStackSize = 8 << 20;

	/// <summary>
	/// How often, in milliseconds, a worker's main thread looks for native work jobs wait for, when nothing else comes.
	/// </summary>
	const int MainThreadWorkPoll = 5;

	/// <summary>
	/// A question about the compilers rather than a compile, of a worker or of the editor, which has the same ones:
	/// "types", each source type managed code takes part in (see <see cref="AssetSystem.ManagedCompilerTypes"/>), or
	/// "fingerprints" of the special dependencies given.
	/// </summary>
	internal static IReadOnlyList<string> Answer( string mode, IEnumerable<string> input ) => mode == "types" ? AssetSystem.ManagedCompilerTypes().ToList() : input.Select( Fingerprint ).ToList();

	/// <summary>
	/// "compiler \t userdata \t name" to its current fingerprint in hex, or "-" if it can't be calculated here.
	/// </summary>
	static string Fingerprint( string line )
	{
		var parts = line.Split( '\t' );
		var fingerprint = parts.Length == 3 && uint.TryParse( parts[1], out var data ) ? IResourceCompilerSystem.SpecialDependencyFingerprint( parts[0], parts[2], data ) : -1;
		return fingerprint < 0 ? "-" : ((uint)fingerprint).ToString( "x8" );
	}

	/// <summary>
	/// Compiles one asset where it is, as the editor's asset system does when it compiles one by itself.
	/// </summary>
	internal static bool Compile( string path ) => IResourceCompilerSystem.CompileFile( path );
}
