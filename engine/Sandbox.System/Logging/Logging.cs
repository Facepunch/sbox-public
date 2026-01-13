using NLog;
using System.Threading.Channels;

namespace Sandbox.Diagnostics;

internal static partial class Logging
{
	static bool _initialized;
	internal static void InitializeConfig()
	{
		if ( _initialized )
			return;

		_initialized = true;

		var config = new NLog.Config.LoggingConfiguration();

#pragma warning disable CA2000 // Dispose objects before losing scope
		// Config takes ownership of targets
		var game_target = new GameLog();
#pragma warning restore CA2000 // Dispose objects before losing scope

		NLog.LogManager.Setup().SetupExtensions( s =>
		{
			s.RegisterLayoutRenderer( "nicestack", ( logEvent ) =>
			{
				var frames = logEvent.StackTrace.GetFrames().Skip( 1 ).Take( 10 ).Where( x => x.GetMethod().DeclaringType.Name != "Logger" );
				var stack = string.Join( "\n", frames.Select( x => $"\t\t{x.GetMethod()?.DeclaringType?.Name}.{x.GetMethod()?.Name} - {x.GetFileName()}:{x.GetFileLineNumber()}" ) );
				if ( stack.StartsWith( "\t\tEngineLoop.Print - " ) ) return "";

				return $"\n{stack}\n";
			} );
		} );

		var appName = Process.GetCurrentProcess().ProcessName.Split( '.' )[0];

		var gamePath = System.Environment.GetEnvironmentVariable( "FACEPUNCH_ENGINE", EnvironmentVariableTarget.User );
		gamePath ??= AppContext.BaseDirectory;

#pragma warning disable CA2000 // Dispose objects before losing scope
		// Config takes ownership of targets
		var file_target = new NLog.Targets.FileTarget
#pragma warning restore CA2000 // Dispose objects before losing scope
		{
			FileName = System.IO.Path.Combine( gamePath, $"logs/{appName}.log" ),
			ArchiveOldFileOnStartup = true,
			OpenFileCacheSize = 10,
			MaxArchiveFiles = 10,
			KeepFileOpen = true,


			//DeleteOldFileOnStartup = true,
			//Layout = "${date:format=yyyy/MM/dd HH\\:mm\\:ss.ffff}\t[${logger}] ${message}\t${exception:format=ToString}${nicestack}"
			Layout = "${date:format=yyyy/MM/dd HH\\:mm\\:ss.ffff}\t[${logger}] ${message}\t${exception:format=ToString}"
		};

		//
		// Targets
		//
		config.AddTarget( "file", file_target );
		config.AddTarget( "console", game_target );
		//config.AddTarget( "null", new NLog.Targets.NullTarget() );

		config.LoggingRules.Clear();

		//
		// Create a logging rule that captures everything
		//
		{
			var rule = new NLog.Config.LoggingRule( "global" );
			rule.LoggerNamePattern = "*";
			rule.EnableLoggingForLevels( NLog.LogLevel.Trace, NLog.LogLevel.Fatal );
			rule.Targets.Add( file_target );
			rule.Targets.Add( game_target );
			//rule.Filters.Add( new WhenMethodFilter( TestLogFilter ) );

			config.LoggingRules.Add( rule );
		}

		NLog.LogManager.Configuration = config;

		//
		// When we quit, shut nlog down
		//
		AppDomain.CurrentDomain.ProcessExit += ( x, y ) =>
		{
			NLog.LogManager.Shutdown();
		};

		SetRule( "*", LogLevel.Info );
	}

	// 
	// Garry: I imagine at some point we'll expose rules in a way where we can choose which systems
	// are which levels. Right now that seems like overkill - so I'm just exposing the ability to change
	// verbosity globally. This is complicated by the fact that I want to pull in all the engine logging
	// system too - hence the OnRulesChanged callback, so we can call shit in the sandbox.engine.dll on change.
	// 

	public static LogLevel GetDefaultLevel()
	{
		if ( Rules.TryGetValue( "*", out var r ) )
			return r;

		return LogLevel.Info;
	}

	public static void SetRule( string wildcard, LogLevel minimumLevel )
	{
		Rules[wildcard] = minimumLevel;
		RuleCache.Clear();
	}

	internal static Dictionary<string, LogLevel> Rules = new();

	static Dictionary<int, bool> RuleCache = new();

	/// <summary>
	/// Return true if we should print this log entry. Use a cache to avoid craziness.
	/// </summary>
	public static bool ShouldLog( string loggerName, LogLevel level )
	{
		lock ( RuleCache )
		{
			var hash = HashCode.Combine( loggerName, level );
			if ( RuleCache.TryGetValue( hash, out var should ) )
				return should;

			should = WorkOutShouldLog( loggerName, level );
			RuleCache[hash] = should;

			return should;
		}
	}

	static bool WorkOutShouldLog( string loggerName, LogLevel level )
	{
		foreach ( var v in Rules.OrderByDescending( x => x.Key.Length ) )
		{
			if ( loggerName.WildcardMatch( v.Key ) && level >= v.Value )
				return true;
		}

		return false;
	}

	// Todo - turn off logging?
	public static bool Enabled { get; set; } = true;
	internal static bool PrintToConsole { get; set; } = true;

	internal static event Action<LogEvent> OnMessage;
	internal static Action<Exception> OnException;

	static Channel<LogEvent> QueuedMessages = Channel.CreateUnbounded<LogEvent>();

	private static int callDepth = 0;

	internal static void Write( in LogEvent e )
	{
		if ( ThreadSafe.IsMainThread && callDepth < 3 )
		{
			try
			{
				callDepth++;
				OnMessage?.Invoke( e );
			}
			finally
			{
				callDepth--;
			}
		}
		else
		{
			QueuedMessages.Writer.TryWrite( e );
		}
	}

	internal static void PushQueuedMessages()
	{
		ThreadSafe.AssertIsMainThread();

		while ( QueuedMessages.Reader.TryRead( out var msg ) )
		{
			try
			{
				OnMessage?.Invoke( msg );
			}
			catch ( System.Exception e )
			{
				Log.Error( e );
			}
		}
	}

	public static Logger GetLogger( string name = null )
	{
		if ( name == null )
		{
			var frame = new System.Diagnostics.StackFrame( 1, false );
			var method = frame.GetMethod();
			name = method.DeclaringType.Name;
		}

		return new Logger( name );
	}

	/// <summary>
	/// Keep a list of loggers
	/// </summary>
	public static HashSet<string> Loggers = new( StringComparer.OrdinalIgnoreCase );

	internal static void RegisterEngineLogger( int id, string v )
	{
		v = $"engine/{v}";
		Loggers.Add( v );
	}

	internal static void RegisterLogger( string name )
	{
		Loggers.Add( name );
	}
}
