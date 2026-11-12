using NativeEngine;
using Sandbox.ContentBuild;
using Sandbox.Engine;
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace Editor;

/// <summary>
/// The editor's content build, the same pipeline as SboxBuild build-content: one build over its writable roots,
/// so a compile that reads another root's output waits for it. Retail editors consume shipped engine content without
/// rebuilding it. It builds everything at startup and again
/// after files change (the asset system counts changes, the frame tick builds them), which rebuilds whatever a
/// change affects, in order. The asset system compiles single assets itself (a load of a changed one, a tool's
/// compile); their outputs record what they were built from, so the build takes them as they are rather than
/// compiling them again. Startup's compiles run in a content worker, as in SboxBuild, with the tool and project code the
/// editor has loaded, so one that crashes fails its source rather than end the editor at every startup; later builds
/// compile a source that crashed a worker in one too. Other compiles run in this process, as a content worker runs
/// them: each on a thread of its own, the main thread doing what they need of it every frame
/// (<see cref="ContentCompilerHost.RunJob"/>).
/// </summary>
internal static class EditorContentBuild
{
	static Project project;
	static bool building;
	static bool pruneOrphans;
	static int builtEpoch = -1;
	static int seenEpoch;
	static RealTimeSince sinceChange;
	static RealTimeUntil retryAfter;
	static HashSet<string> current = new( StringComparer.OrdinalIgnoreCase );
	static HashSet<string> sharedOutputs = new( StringComparer.OrdinalIgnoreCase );
	static ResourceCompiler compiler;
	static string compilerKey;
	static Task incrementalBuild;
	static CancellationTokenSource cancellation;
	static RealTimeSince buildStarted;
	static PopupToast progress;

	/// <summary>
	/// Changes usually come in bursts (a save writes several files, a checkout many)
	/// </summary>
	const float Debounce = 0.25f;

	/// <summary>
	/// A build that couldn't run (another build holds a root) tries again after this, not every frame
	/// </summary>
	const float RetryDelay = 2.0f;

	/// <summary>
	/// Compiles at once while the editor is open. Startup, which waits for its build, runs up to as many as SboxBuild does.
	/// </summary>
	const int Jobs = 4;

	/// <summary>
	/// Memory left free beside the compiles: one that runs in the editor's process and fails to allocate ends the editor.
	/// </summary>
	const long MemoryReserve = 3L << 30;

	public static bool IsBuilding => building;

	/// <summary>
	/// Builds everything before the editor opens, waiting for any other build of the same roots.
	/// </summary>
	public static async Task Startup( Project project )
	{
		EditorContentBuild.project = null;
		cancellation?.Cancel();
		if ( incrementalBuild is not null ) await Pumping( incrementalBuild );
		incrementalBuild = null;

		// ContentBuild has no engine dependency, but its editor tasks must survive play-mode context resets.
		Sandbox.Tasks.ExpirableSynchronizationContext.AllowPersistentTaskMethods( typeof( ContentBuilder ).Assembly );
		EditorContentBuild.project = project;

		await Pumping( Update( true ) );
	}

	/// <summary>
	/// Awaits a build, doing what its compiles need of the main thread meanwhile: the frame doesn't run yet at startup.
	/// </summary>
	static async Task Pumping( Task build )
	{
		while ( !build.IsCompleted )
		{
			ContentCompilerHost.Pump( 5 );
			await Task.Yield();
		}
		await build;
	}

	/// <summary>
	/// A file in another output root that a record still lists, which the orphan collector has to leave.
	/// </summary>
	public static bool IsOwned( string path ) => sharedOutputs.Contains( Path.GetFullPath( path ) );

	/// <summary>
	/// A single asset the user compiles needs the roots this build holds: the build stops once its running compiles are
	/// published, doing what they need of the main thread meanwhile, and starts again by itself afterwards. False if no
	/// build of this editor's holds them, or it can't end from here (called from inside the build's own work, or from an
	/// async continuation, where the build's own can't run).
	/// </summary>
	internal static bool Yield()
	{
		if ( !ThreadSafe.IsMainThread || publishing || ContentCompilerHost.IsPumping || Sandbox.Tasks.ExpirableSynchronizationContext.IsProcessing ) return false;
		if ( !building || incrementalBuild is not { IsCompleted: false } ) return false;

		cancellation?.Cancel();
		RealTimeSince waited = 0;
		while ( building && waited < YieldTimeout )
		{
			ContentCompilerHost.Pump( 5 );
			Sandbox.Tasks.SyncContext.MainThread?.ProcessQueue();
		}

		if ( building ) Log.Warning( $"The content build's running compiles didn't finish within {YieldTimeout}s, so the requested compile can't run yet" );
		return !building;
	}

	const float YieldTimeout = 10.0f;

	/// <summary>
	/// Inside the running build's publication, which goes no further until it returns.
	/// </summary>
	static bool publishing;

	static void Published( IReadOnlyList<string> paths )
	{
		publishing = true;
		try { IAssetSystem.RegisterBuildOutputs( string.Join( "\n", paths ) ); }
		finally { publishing = false; }
	}

	[EditorEvent.Frame]
	static void Frame()
	{
		if ( project is null ) return;
		if ( building )
		{
			ContentCompilerHost.Pump();
			if ( incrementalBuild is { IsCompleted: false } && buildStarted > 1 )
				progress ??= new PopupToast { Title = "Compiling assets", Icon = "build" };
			if ( progress?.GetCancel().IsCancellationRequested == true ) cancellation?.Cancel();
			return;
		}

		if ( pruneOrphans )
		{
			pruneOrphans = false;
			AssetSystem.Tick();
			AssetSystem.DeleteOrphans();
		}

		var epoch = IAssetSystem.GetContentChangeEpoch();
		if ( epoch != seenEpoch )
		{
			seenEpoch = epoch;
			sinceChange = 0;
		}

		if ( epoch != builtEpoch && sinceChange > Debounce && retryAfter ) incrementalBuild = Update( false );
	}

	/// <summary>
	/// One build over every mounted root, then everything the asset system needs to know about it: each source's
	/// verdict and the files it published, which reloads what's loaded. Nothing compiles itself on load meanwhile.
	/// </summary>
	static async Task Update( bool startup )
	{
		building = true;
		startupBuild = startup;
		buildStarted = 0;
		cancellation = new CancellationTokenSource();
		var generation = codeGeneration;
		IAssetSystem.SuspendOnDemandCompiles( true );

		try
		{
			// Changes made before this build (the .meta files asset registration writes) are ones it sees, so count
			// them now rather than when the watcher next delivers them
			IAssetSystem.RunFrame();
			var epoch = IAssetSystem.GetContentChangeEpoch();

			var roots = Roots( project );

			// A single asset compiled by the asset system holds the roots' locks, as this build does
			IAssetSystem.SetContentRootLocks( string.Join( "\n", roots.Select( ContentBuilder.LockFile ) ) );

			var request = new BuildRequest
			{
				Roots = roots,
				ReadOnlyMounts = Sandbox.Application.IsRetail ? ContentRoots.Engine( EngineFileSystem.Root.GetFullPath( "/" ) )
					.Where( x => !roots.Any( r => string.Equals( r.Path, x.Path, StringComparison.OrdinalIgnoreCase ) ) )
					.Select( x => (x.Mod, x.Path) ).ToList() : [],

				// Game resources anywhere generate their textures into the open project's transient folder
				OutputRoots = new Dictionary<string, string> { ["transient"] = Path.Combine( project.GetRootPath(), ".sbox", "transient" ) },

				// The engine's shipped transients, which every project but the menu mounts after its own
				OutputFallbacks = project.Config.Ident == "menu" ? new Dictionary<string, string>() : new Dictionary<string, string> { ["transient"] = EngineFileSystem.Root.GetFullPath( "addons/menu/Transients" ) },

				// What each job publishes is known before the next frame runs, so its file events aren't changes
				Published = Published,
				MaxJobs = JobLimit( startup ? ResourceCompiler.DefaultMaxJobs : Jobs ),
				CanStartJob = () => AvailableMemory().Commit > MemoryReserve,

				// Only startup waits for another build, with the splash screen up; the editor never freezes on one
				LockTimeout = startup ? TimeSpan.FromMinutes( 30 ) : TimeSpan.Zero,
				Waiting = startup ? root =>
				{
					EditorSplashScreen.SetMessage( $"Waiting for another content build of {root}" );
					IToolsDll.Current?.Spin();
				}
				: null,
				JobFinished = source =>
				{
					if ( startup ) EditorSplashScreen.SetMessage( $"Compiled {source}" );
					else if ( progress is not null ) progress.Subtitle = $"Compiled {source}";
				},
			};

			// The last build's workers are gone before this one takes any
			var stopping = workersStopping;
			workersStopping = null;
			if ( stopping is not null ) await stopping;
			var assetCompiler = Compiler( request.Mounts );
			BuildReport report;
			try
			{
				report = await ContentBuilder.BuildAsync( request, assetCompiler, cancellation.Token );
			}
			finally
			{
				// Workers go after the build that started them; the editor opens while startup's shuts down
				workersStopping = Task.Run( assetCompiler.Finished );
			}

			Report( report, roots, startup );

			if ( report.Error is not null || report.Cancelled )
			{
				// An explicit cancel waits for another edit; a lock conflict or hotload retries automatically.
				if ( progress?.GetCancel().IsCancellationRequested == true && generation == codeGeneration ) builtEpoch = epoch;
				else retryAfter = RetryDelay;
				return;
			}

			Publish( report, epoch );
			if ( generation == codeGeneration ) builtEpoch = epoch;
			pruneOrphans |= report.Changed.Count > 0;
		}
		catch ( Exception e )
		{
			Log.Error( e, "Content build failed" );
			retryAfter = RetryDelay;
		}
		finally
		{
			progress?.Dispose();
			progress = null;
			cancellation.Dispose();
			cancellation = null;
			IAssetSystem.SuspendOnDemandCompiles( false );
			building = false;
		}
	}

	/// <summary>
	/// Compiles at once, up to a limit: one per 2 GB beyond the reserve of what is free now, after the editor's own use.
	/// </summary>
	static int JobLimit( int limit )
	{
		var (physical, commit) = AvailableMemory();
		return (int)Math.Clamp( (Math.Min( physical, commit ) - MemoryReserve) / (2L << 30), 1, limit );
	}

	/// <summary>
	/// Physical memory free system wide and commit this process can still make. Unknown, so unlimited, off Windows.
	/// </summary>
	static (long Physical, long Commit) AvailableMemory()
	{
		var status = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
		if ( !OperatingSystem.IsWindows() || !GlobalMemoryStatusEx( ref status ) ) return (long.MaxValue, long.MaxValue);
		return ((long)status.AvailPhys, (long)status.AvailPageFile);
	}

	[StructLayout( LayoutKind.Sequential )]
	struct MemoryStatusEx
	{
		public uint Length, MemoryLoad;
		public ulong TotalPhys, AvailPhys, TotalPageFile, AvailPageFile, TotalVirtual, AvailVirtual, AvailExtendedVirtual;
	}

	[DllImport( "kernel32.dll" )]
	static extern bool GlobalMemoryStatusEx( ref MemoryStatusEx status );

	static void Publish( BuildReport report, int epoch )
	{
		// Only what's current is the build's to say; why anything else isn't is for a compile of it to find out
		foreach ( var source in report.Current )
			IAssetSystem.SetAssetBuiltAt( source, epoch );

		// No longer current: failed now, gone, or not built here any more (a root was unmounted)
		foreach ( var source in current.Where( x => !report.Current.Contains( x ) ) )
			IAssetSystem.SetAssetBuiltAt( source, -1 );

		current = report.Current;
		sharedOutputs = report.SharedOutputs;

	}

	static void Report( BuildReport report, List<ContentRoot> roots, bool startup )
	{
		if ( startup )
		{
			foreach ( var root in roots )
			{
				int Count( List<string> list ) => list.Count( x => x.StartsWith( $"{root.Mod}:", StringComparison.OrdinalIgnoreCase ) );
				Log.Info( $"Content {root.Mod}: {Count( report.Jobs )} compiles, {Count( report.Failed )} failed, {Count( report.Blocked )} blocked, {Count( report.Adopted )} adopted, {Count( report.Described )} described, {Count( report.Removed )} removed, {Count( report.Unsupported )} unsupported" );
			}
			Log.Info( $"Content build: {report.DescribeMisses} describe misses" );
		}
		else if ( report.Jobs.Count > 0 || report.Removed.Count > 0 || report.Adopted.Count > 0 )
		{
			Log.Info( $"Content build: {report.Jobs.Count} compiles, {report.Adopted.Count} adopted, {report.Failed.Count} failed, {report.Blocked.Count} blocked, {report.Removed.Count} removed" );
		}

		if ( report.Error is not null ) Log.Warning( $"Content wasn't built: {report.Error}" );
		else if ( report.Failed.Count > 0 || report.Blocked.Count > 0 ) Log.Warning( report.Log.ToString() );
	}

	/// <summary>
	/// Writable roots in mount order: engine roots in development builds, then libraries and the project.
	/// </summary>
	static List<ContentRoot> Roots( Project project ) => ContentRoots.WithProjects(
		Sandbox.Application.IsRetail ? [] : ContentRoots.Engine( EngineFileSystem.Root.GetFullPath( "/" ) ),
		LibrarySystem.All.Select( x => x.Project ).Append( project ).Where( x => x.HasAssetsPath() )
			.Select( x => (x.Config.FullIdent, x.GetAssetsPath(), x.GetRootPath()) ) );

	/// <summary>
	/// Hotloads so far: a hotload cancels a build that's running (none happens during a managed compile), and the next runs
	/// the new code.
	/// </summary>
	static int codeGeneration;

	/// <summary>
	/// Whether the build running is startup's.
	/// </summary>
	static bool startupBuild;

	/// <summary>
	/// Sources whose compile crashed every worker it ran on (or ran it out of memory), which compile in a worker until
	/// a compile of one succeeds.
	/// </summary>
	static readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> lost = new( StringComparer.OrdinalIgnoreCase );

	/// <summary>
	/// The last build's workers shutting down.
	/// </summary>
	static Task workersStopping;

	/// <summary>
	/// The tool and project assemblies the workers load: this editor's own copy of the ones it compiled and loaded.
	/// </summary>
	static string Compilers => Path.Combine( Path.GetTempPath(), "sbox-compilers", $"editor-{Environment.ProcessId}" );
	static int compilerSets;

	/// <summary>
	/// The compiler for these mounts and the code the editor has loaded now. Startup's compiles run in a content worker,
	/// later builds' in this process but for the sources in <see cref="lost"/>. Adopting what's built, and questions
	/// about the compilers, are answered here.
	/// </summary>
	static ResourceCompiler Compiler( IReadOnlyList<(string Mod, string Path)> mounts )
	{
		var key = string.Join( "\n", mounts.Select( x => $"{x.Mod}|{x.Path}" ).Append( $"{codeGeneration}" ) );
		if ( key == compilerKey ) return compiler;

		// The old workers go before the assemblies they loaded
		StopWorkers();
		DeleteLeftovers();

		// A folder of its own, so a worker or scanner still holding the last set's files can't fail this one
		var folder = Path.Combine( Compilers, $"{++compilerSets}" );
		Directory.CreateDirectory( folder );
		foreach ( var assembly in ToolsDll.LoadedAssemblies.Where( x => x.CompiledAssemblyBytes is not null ) )
			File.WriteAllBytes( Path.Combine( folder, $"{assembly.Assembly.GetName().Name}.dll" ), assembly.CompiledAssemblyBytes );

		compilerKey = key;
		// The editor has a GPU, and its projects' image generators can copy compiled textures
		var worker = OperatingSystem.IsWindows() ? "contentworker.exe" : "contentworker";
		compiler = new ResourceCompiler( EngineFileSystem.Root.GetFullPath( $"bin/managed/{worker}" ), new AssetTypeTable( EngineFileSystem.Root.GetFullPath( "bin/assettypes.txt" ) ), mounts, folder )
		{
			RenderDevice = true,
			InProcess = ( mode, source, staging, report, args ) => ContentCompilerHost.RunJob( mode, source, staging, report, args ),
			InWorker = ( mode, source ) => startupBuild ? mode is "compile" or "describe" : lost.ContainsKey( source ),
			Lost = lost,
		};
		return compiler;
	}

	/// <summary>
	/// The folders of editors that are gone without deleting theirs (closed with their workers still letting go of it).
	/// </summary>
	static void DeleteLeftovers()
	{
		var parent = Path.GetDirectoryName( Compilers );
		if ( !Directory.Exists( parent ) ) return;

		foreach ( var folder in Directory.GetDirectories( parent, "editor-*" ) )
		{
			if ( !int.TryParse( Path.GetFileName( folder )["editor-".Length..], out var id ) || id <= 0 || Running( id ) ) continue;
			try { Directory.Delete( folder, true ); } catch ( IOException ) { } catch ( UnauthorizedAccessException ) { }
		}
	}

	static bool Running( int processId )
	{
		try { using var process = System.Diagnostics.Process.GetProcessById( processId ); return !process.HasExited; }
		catch ( ArgumentException ) { return false; }
	}

	/// <summary>
	/// Code changed (compilers and resource types may have come or gone): the next build runs with it.
	/// Code alone doesn't trigger a build or retry failed assets. New asset types are registered by ToolsDll, which
	/// counts any newly recognized sources as content changes; sources compiled from now on run the new code.
	/// </summary>
	[EditorEvent.Hotload]
	static void CodeChanged()
	{
		codeGeneration++;
		if ( incrementalBuild is { IsCompleted: false } ) cancellation?.Cancel();
	}

	[Event( "app.exit" )]
	static void Shutdown()
	{
		// A build left unfinished is finished by the next one (a publication it interrupted rolls back), but compiles
		// running in this process finish before the engine they run in shuts down
		project = null;
		cancellation?.Cancel();
		ContentCompilerHost.WaitForJobs();
		incrementalBuild = null;
		StopWorkers();
	}

	static void StopWorkers()
	{
		workersStopping?.Wait();
		compiler?.Finished();
		compiler = null;
		compilerKey = null;

		try { Directory.Delete( Compilers, true ); } catch ( IOException ) { } catch ( UnauthorizedAccessException ) { }
	}
}
