using Facepunch.ActionGraphs;
using Sandbox.Internal;
using Sentry;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Sandbox;

[SkipHotload]
internal class HotloadManager : IDisposable
{
	/// <summary>
	/// Assert if any one type takes more than this duration to process, AND more than
	/// <see cref="PerTypeAssertThresholdFraction"/> of the total time.
	/// </summary>
	private const int PerTypeAssertThresholdMillis = 500;

	/// <summary>
	/// Assert if any one type takes more than this fraction of the total time to process,
	/// AND more than <see cref="PerTypeAssertThresholdMillis"/>.
	/// </summary>
	private const float PerTypeAssertThresholdFraction = 0.8f;

	static HotloadManager()
	{
		Hotload.AssemblyNameFormatter = FormatAssemblyName;
	}

	public void Dispose()
	{
		OnSuccess = null;

		//Hotload?.Dispose();
		Hotload = null;
	}

	private static string FormatAssemblyName( AssemblyName name )
	{
		if ( name == null )
		{
			return "null";
		}

		if ( name.Name.StartsWith( "package.", StringComparison.OrdinalIgnoreCase ) )
		{
			return $"{name.Name} v{name.Version}";
		}

		return name.Name;
	}

	Logger log;

	[ConVar( ConVarFlags.Protected, Min = 0, Max = 2, Help = "Hotload log level (0: none, 1: simple, 2: full)" )]
	public static int hotload_log { get; set; }

	[ConVar( "hotload_fast", ConVarFlags.Saved | ConVarFlags.Protected, Help = "Experimental fast hotloads if only method bodies change" )]
	public static bool hotload_fast { get; set; } = true;

	public string Name { get; }

	public bool LoggingEnabled => hotload_log > 0;
	public bool VerboseLoggingEnabled => hotload_log > 1;

	public bool NeedsSwap { get; set; }
	public Hotload Hotload { get; protected set; }
	public event Action OnSuccess;
	public event Action OnFail;
	public event Action PreSwap;

	public Mono.Cecil.IAssemblyResolver AssemblyResolver
	{
		get => Hotload.AssemblyResolver;
		set => Hotload.AssemblyResolver = value;
	}

	public HotloadManager( string name = "Hotload" )
	{
		log = new Logger( $"hotload/{name}" );
		Name = name;

		Hotload = new Hotload( logger: log );
		Hotload.AddDefaultUpgraders();
		Hotload.AddDefaultWatchAssemblies();
		Hotload.AddDefaultSkips();
	}

	internal void Watch( Assembly assembly )
	{
		log.Trace( $"Watching {assembly}" );
		Hotload.WatchAssembly( assembly );
	}

	internal void Ignore( Assembly gameAssembly )
	{
		if ( gameAssembly == null ) return;

		log.Trace( $"Ignoring {gameAssembly}" );
		Hotload.IgnoreAssembly( gameAssembly );
	}

	internal void Ignore( string asmName )
	{
		if ( asmName == null ) return;

		log.Trace( $"Ignoring {asmName}" );
		Hotload.IgnoreAssembly( asmName );
	}

	internal void Ignore<T>()
	{
		Ignore( typeof( T ).Assembly );
	}

	/// <summary>
	/// Runs pending hotload, invoking an optional callback after migration and before success notifications.
	/// Individual upgrade errors are logged. A completed migration still runs the callbacks so the new types are registered.
	/// </summary>
	/// <param name="beforeSuccess">Called after reference migration and before normal hotload callbacks.</param>
	/// <returns>Returns false for a notification failure without a registration callback. An interrupted migration propagates to the caller.</returns>
	public bool DoSwap( Action beforeSuccess = null )
	{
		log.Trace( "DoSwap Start" );

		// The render thread drawing a frame is joined first and stays parked until the next frame's hand-over, so nothing
		// draws while references are swapped (docs/reference/borealis-render-thread.md)
		Sandbox.BorealisGpuRenderBackend.JoinRenderThread();
		MainThread.RunQueues(); // dispose of any threaded IDisposables waiting

		var timer = Stopwatch.StartNew();
		var eventRecord = new Api.Events.EventRecord( $"Hotload.{Name}" );

		using var sentryScope = SentrySdk.PushScope();

		lock ( Hotload )
		{
			using var _timer = eventRecord.ScopeTimer( "Time" );

			NeedsSwap = false;

			{
				using var timerThreads = eventRecord.ScopeTimer( "PreSwap" );
				PreSwap?.Invoke();
			}

			var workerThreadsStarted = WorkerThread.HasStarted;

			// Need worker threads to stop while hotloading
			{
				using var timerThreads = eventRecord.ScopeTimer( "WorkerThreadStop" );
				WorkerThread.Stop( 1000 );
			}

			var unloads = Hotload.GetOutgoingAssemblies();

			foreach ( var assm in unloads )
			{
				log.Trace( $"Will Unload {assm}" );
			}

			Hotload.TracePaths = true;
			Hotload.TraceRoots = VerboseLoggingEnabled;
			Hotload.IncludeTypeTimings = VerboseLoggingEnabled;
			Hotload.IncludeProcessorTimings = VerboseLoggingEnabled;

			//
			// Give hotload special info in sentry, searchable by group:hotload
			//
			SentrySdk.ConfigureScope( x =>
			{
				x.SetTag( "group", "hotload" );

				x.Contexts["Hotload"] = new
				{
					HotloadManager = Name,
					FastEnabled = hotload_fast
				};
			} );

			HotloadResult info = null;
			try
			{

				using ( var t = eventRecord.ScopeTimer( "UpdateReferences" ) )
				{
					Json.StopWarmUp();
					info = Hotload.UpdateReferences();
				}

				foreach ( var scene in Scene.AllLive )
					scene.RebuildTraceProviders();

				if ( info.NoAction )
				{
					eventRecord = null; // don't bother with the event
					beforeSuccess?.Invoke();
					return true;
				}

				// stats
				// list of assembies that are hotloading? count of assemblies?
				eventRecord.SetValue( "InstancesProcessed", info.InstancesProcessed );
				eventRecord.SetValue( "ProcessingTime", info.ProcessingTime );
				eventRecord.SetValue( "InstanceQueueTime", info.InstanceQueueTime );
				eventRecord.SetValue( "StaticFieldTime", info.StaticFieldTime );
				eventRecord.SetValue( "WatchedInstanceTime", info.WatchedInstanceTime );
				eventRecord.SetValue( "DiagnosticsTime", info.DiagnosticsTime );
				eventRecord.SetValue( "Success", info.Success );

				foreach ( var (typeName, entry) in info.TypeTimings )
				{
					var fraction = entry.Milliseconds / info.ProcessingTime;

					if ( fraction >= PerTypeAssertThresholdFraction && entry.Milliseconds >= PerTypeAssertThresholdMillis )
					{
						Log.Error( new Exception( $"Type {typeName} is taking too long to hotload" ), $"Type {typeName} is taking too long to hotload\n  Processing time: {entry.Milliseconds:F1}ms, {entry.Milliseconds * 100d / info.ProcessingTime:F1}% of the total" );
					}
				}

				if ( info.InstancesProcessed > 0 && LoggingEnabled )
				{
					log.Info( $"{Name} processed {info.InstancesProcessed:n0} instances in {(info.ProcessingTime / 1000):0.00}s" );

					if ( VerboseLoggingEnabled )
					{
						log.Info( $"   Instance queue: {info.InstanceQueueTime:0.0}ms" );
						log.Info( $"        Diagnostics: {info.DiagnosticsTime:0.0}ms" );
						log.Info( $"    Static fields: {info.StaticFieldTime:0.0}ms" );
						log.Info( $"Watched instances: {info.WatchedInstanceTime:0.0}ms" );

						LogVerboseTimingInfo( info.TypeTimings );
					}
				}
				else
				{
					log.Trace( $"processed {info.InstancesProcessed:n0} instances in {(info.ProcessingTime / 1000):0.00}s" );
				}

				{
					using var timerSuccess = eventRecord.ScopeTimer( "OnSuccess" );
					beforeSuccess?.Invoke();
					OnSuccess?.Invoke();
				}
			}
			catch ( System.Exception e ) when ( e is not OutOfMemoryException )
			{
				log.Warning( e, "Hotload exception" );
				Sentry.SentrySdk.CaptureException( e );

				OnFail?.Invoke();
				if ( info is null || beforeSuccess is not null )
				{
					throw;
				}
				return false;
			}
			finally
			{
				if ( workerThreadsStarted )
				{
					using var timerThreads = eventRecord?.ScopeTimer( "WorkerThreadStart" );
					// Resume worker threads
					WorkerThread.Start();
				}

				eventRecord?.Submit();
				log.Trace( "DoSwap Finished" );

				CodeIterate.Hint( $"{Name}Hotload", timer.Elapsed.TotalMilliseconds );

				if ( Name == "Client" )
				{
					CodeIterate.Finish();
				}
			}
		}

		return true;
	}

	private static string FormatMessageGroup( string message, IEnumerable<HotloadResultEntry> entries )
	{
		var countString = entries.Count() == 1 ? "" : $" (x{entries.Count()})";

		return $"{message}{countString}{Environment.NewLine}  {string.Join( $"{Environment.NewLine}{Environment.NewLine}  ", entries.Select( x => x.Context?.Replace( "\n", "\n  " ) ) )}";
	}

	private static string EscapeForLog( string value )
	{
		return value
			.Replace( "<", "[" )
			.Replace( ">", "]" );
	}

	private void LogVerboseTimingInfo( Dictionary<string, InstanceTimingEntry> entries )
	{
		const int maxEntries = 20;
		const int maxRoots = 10;

		var timingEntries = entries
			.OrderByDescending( x => x.Value.Milliseconds )
			.Take( maxEntries )
			.Select( x => new
			{
				x.Key,
				x.Value,
				Roots = x.Value.Roots
					.OrderByDescending( y => y.Value.Milliseconds )
					.Take( maxRoots )
					.ToArray()
			} )
			.ToArray();

		var nameWidth = timingEntries.Max( x => Math.Max( x.Key.Length + 2, x.Roots.Max( y => y.Key.Length ) + 2 ) );
		var instancesWidth = timingEntries.Max( x => Math.Max( x.Value.Instances, x.Roots.Max( y => y.Value.Instances ) ) ).ToString().Length;
		var timeWidth = timingEntries.Max( x => Math.Max( x.Value.Milliseconds, x.Roots.Max( y => y.Value.Milliseconds ) ) ).ToString( "0.0" ).Length;
		var totalWidth = nameWidth + instancesWidth + timeWidth + 12;
		var hdiv = new string( '-', totalWidth );

		log.Info( hdiv );

		foreach ( var timingEntry in timingEntries )
		{
			log.Info( $"| {EscapeForLog( timingEntry.Key.PadRight( nameWidth ) )} " +
					  $"| {timingEntry.Value.Instances.ToString().PadLeft( instancesWidth )} " +
					  $"| {timingEntry.Value.Milliseconds.ToString( "0.0" ).PadLeft( timeWidth )}ms |" );

			if ( timingEntry.Value.Roots == null ) continue;

			foreach ( var rootEntry in timingEntry.Roots )
			{
				log.Info( $"|   {EscapeForLog( rootEntry.Key.PadRight( nameWidth - 2 ) )} " +
						  $"| {rootEntry.Value.Instances.ToString().PadLeft( instancesWidth )} " +
						  $"| {rootEntry.Value.Milliseconds.ToString( "0.0" ).PadLeft( timeWidth )}ms |" );
			}

			if ( timingEntry.Value.Roots.Count > maxRoots )
			{
				log.Info( $"|   ... and {timingEntry.Value.Roots.Count - maxRoots} other roots".PadRight( totalWidth - 1 ) + "|" );
			}

			log.Info( hdiv );
		}

		if ( entries.Count > maxEntries )
		{
			log.Info( $"| ... and {entries.Count - maxEntries} other types".PadRight( totalWidth - 1 ) + "|" );
			log.Info( hdiv );
		}
	}

	/// <summary>
	/// Lets the hotload system know that something has changed. If we detect that we are
	/// replacing a dll (instead of just adding one) we'll queue up a swap. We should still
	/// call this even if <paramref name="oldAssembly"/> or <paramref name="newAssembly"/>
	/// is null, since that will tell the hotload system to start / stop watching static
	/// members of the assembly.
	/// </summary>
	internal void Replace( [AllowNull] Assembly oldAssembly, [AllowNull] Assembly newAssembly )
	{
		log.Trace( $"Replace {FormatAssemblyName( oldAssembly?.GetName() )} with {FormatAssemblyName( newAssembly?.GetName() )}" );

		if ( Hotload.ReplacingAssembly( oldAssembly, newAssembly ) )
		{
			NeedsSwap = true;
		}
	}
}

internal static class HotloadExtensions
{
	extension( Hotload hotload )
	{
		/// <summary>
		/// Add Sandbox-specific instance upgraders defined in the <c>Sandbox.Reflection</c> project.
		/// </summary>
		public void AddDefaultUpgraders()
		{
			hotload.AddUpgraders( typeof( TypeLibrary ).Assembly );
		}

		/// <summary>
		/// Watch static fields in Sandbox assemblies so we can find instances to upgrade.
		/// </summary>
		public void AddDefaultWatchAssemblies()
		{
			hotload.WatchAssembly( "Sandbox.Engine" );
			hotload.WatchAssembly( "Sandbox.System" );
			hotload.WatchAssembly( typeof( Sandbox.Internal.EventSystem ).Assembly );
		}

		/// <summary>
		/// Ignore assemblies and types that we know can't contain instances that need
		/// upgrading, or that we intentionally don't want to upgrade for whatever reason.
		/// </summary>
		public void AddDefaultSkips()
		{
			// This will skip types that it automatically decides are safe. This is
			// just for performance, so if it seems over-zealous just comment out this
			// line until it's fixed.

			hotload.AddUpgrader<Upgraders.AutoSkipUpgrader>();

			// Ignore these assemblies because they can't hold anything
			// that can be swappable.

			hotload.IgnoreAssembly<NLog.Logger>();
			hotload.IgnoreAssembly<Sentry.SentryStackFrame>();
			hotload.IgnoreAssembly<System.Net.Http.HttpClient>();

			// The runtime's sockets and pipes: their state is the runtime's own, used by its threads (the socket engine's
			// event loop holds its event queue, so a copy left every socket in the process deaf)
			hotload.IgnoreAssembly<System.Net.Sockets.Socket>();
			hotload.IgnoreAssembly<System.IO.Pipes.NamedPipeServerStream>();
			hotload.IgnoreAssembly<System.Text.RegularExpressions.Regex>();
			hotload.IgnoreAssembly<LiteDB.LiteDatabase>();
			hotload.IgnoreAssembly<SkiaSharp.SKBitmap>();
			hotload.IgnoreAssembly<Microsoft.CodeAnalysis.Diagnostic>();
			hotload.IgnoreAssembly<Refit.PostAttribute>();
			hotload.IgnoreAssembly<Nav.NavMesh>();

			var skipUpgrader = hotload.GetUpgrader<Upgraders.SkipUpgrader>();

			// Skip types that reference things that often cause errors during hotload.

			skipUpgrader.AddSkippedType<ExceptionDispatchInfo>();
			skipUpgrader.AddSkippedType<NodeLibrary>();
			skipUpgrader.AddSkippedType<NodeDefinition>();

			// Compiler plans are rebuilt on binding invalidation. Runtime stores still migrate normally.

			foreach ( var type in Breen.ScriptSystem.CompilationCacheTypes )
			{
				skipUpgrader.AddSkippedType( type );
			}

			// These are safe to skip for speed.

			skipUpgrader.AddSkippedType<JsonNode>();
			skipUpgrader.AddSkippedType<JsonElement>();
			skipUpgrader.AddSkippedType<JsonObject>();
			skipUpgrader.AddSkippedType<JsonArray>();
		}
	}
}
