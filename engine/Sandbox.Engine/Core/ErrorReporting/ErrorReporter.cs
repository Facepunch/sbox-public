using Sentry;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using static Sandbox.Api.Events;

namespace Sandbox.Engine;

[SkipHotload]
internal static class ErrorReporter
{
#if RETAIL
	internal static bool IsUsingSentry => !Application.IsStandalone && !Application.IsUnitTest;
	internal static string ManagedDsn => "https://4f8440da406da20cfc2834a214f2ffad@o13219.ingest.sentry.io/5715364";
#else
	internal static bool IsUsingSentry => false;
	internal static string ManagedDsn => null;
#endif

	public static void Initialize()
	{
		if ( !IsUsingSentry ) return;

		SentrySdk.Init( config =>
		{
			config.Dsn = ManagedDsn;

			config.AutoSessionTracking = true;
			config.SetBeforeSend( BeforeSend );
			config.Release = Application.Version;

			//
			// with this enabled, engine2.dll will fail to load. I don't know why or how.
			// all I know is that it took me 3 hours to figure it out.
			//
			config.DetectStartupTime = StartupTimeDetectionMode.None;
		} );

		Logging.OnException = ReportException;
	}

	public static void Flush()
	{
		if ( !IsUsingSentry ) return;

		SentrySdk.Flush();
	}

	private static SentryEvent BeforeSend( SentryEvent ev, SentryHint hint )
	{
		ev.Release = Application.Version;
		ev.Environment = Application.IsEditor ? "editor" : "game";
		ev.SetTag( "protocolapi", $"{Protocol.Api}" );
		ev.SetTag( "protocolnet", $"{Protocol.Network}" );
		ev.SetTag( "releasedate", $"{Application.VersionDate}" );
		ev.SetTag( "game", $"{Application.GameIdent}" );
		ev.SetTag( "gameversion", $"{Application.GamePackage?.Revision?.VersionId}" );
		ev.SetTag( "host", Application.IsDedicatedServer ? "dedicated" : "game" );

		ev.Contexts.Gpu.Name = SystemInfo.Gpu;
		ev.Contexts.Gpu.Version = SystemInfo.GpuVersion;
		ev.Contexts.Gpu.MemorySize = (int)(SystemInfo.GpuMemory / (1024 * 1024));

		ev.Contexts.Device.CpuDescription = SystemInfo.ProcessorName;
		ev.Contexts.Device.ProcessorCount = (int)Environment.ProcessorCount;
		ev.Contexts.Device.ProcessorFrequency = (int)SystemInfo.ProcessorFrequency;
		ev.Contexts.Device.DeviceType = "Desktop";
		ev.Contexts.Device.MemorySize = (int)(SystemInfo.TotalMemory / (1024 * 1024));

		ev.Contexts.Device.StorageSize = SystemInfo.StorageSizeTotal;
		ev.Contexts.Device.FreeStorage = SystemInfo.StorageSizeAvailable;

		return ev;
	}

	// quickish hackish
	static List<int> errorDeduplicate = new();

	internal static void ResetCounters()
	{
		errorDeduplicate.Clear();
	}

	public static void ReportException( Exception exception )
	{
		Application.ExceptionCount++;

		if ( !IsUsingSentry ) return;

		// If the exception is a ?.Invoke() - we're really only interested
		// in the underlying error, not the invocation error. So report that instead.
		if ( exception is TargetInvocationException ex )
		{
			exception = exception.InnerException;
		}

		StackTrace stackTrace = new StackTrace( exception, true );

		//
		// Deduplicate these errors
		//
		{
			HashCode hc = new HashCode();
			hc.Add( exception.GetType() );
			hc.Add( exception.Message );

			foreach ( var frame in stackTrace.GetFrames() )
			{
				hc.Add( frame.GetFileName() );
				hc.Add( frame.GetFileLineNumber() );
			}

			var fingerprint = hc.ToHashCode();

			if ( errorDeduplicate.Count( x => x == fingerprint ) > 2 )
			{
				// todo close game if running, return to menu
				return;
			}

			errorDeduplicate.Add( fingerprint );
		}

		//
		// If this is a package exception, we report it to our backend
		// rather than to sentry.
		//
		if ( TryReportPackageException( exception, stackTrace, out string source ) )
			return;

		// We should only get engine/menu errors via this now!

		SentrySdk.CaptureException( exception, ( scope ) =>
		{
			// The source is a way for us to filter the exceptions by menu, engine, tools or addon
			scope.SetTag( "source", source );
		} );
	}

	private static bool TryReportPackageException( Exception exception, StackTrace stackTrace, out string source )
	{
		source = "engine";

		const string menuAssembly = "package.local.menu";
		const string toolsAssembly = "package.toolbase";

		StackFrame[] frames = stackTrace.GetFrames();
		if ( frames.Length == 0 ) return false;

		Assembly[] assm = frames.Select( x => x?.GetMethod()?.DeclaringType?.Assembly ).Distinct().ToArray();
		string[] assemblies = assm.Select( x => x.GetName().Name ).ToArray();

		if ( assemblies.Any( x => x.StartsWith( menuAssembly ) ) ) source = "menu";
		if ( assemblies.Any( x => x.Contains( "Sandbox.Tools" ) || x.StartsWith( toolsAssembly ) ) ) source = "tools";

		//
		// No game loaded?
		//
		if ( string.IsNullOrEmpty( Application.GameIdent ) )
		{
			source = "menu";
			return false;
		}

		//
		// Find all addons involved. The assmebly name really isn't a good measure of this right now. We can do better.
		// and we actually have to do a lot better when tier 1 addons are added.
		//
		foreach ( var a in assm )
		{
			var name = a.GetName().Name;
			if ( name.StartsWith( menuAssembly ) ) continue;
			if ( name.StartsWith( toolsAssembly ) ) continue;
			if ( name.StartsWith( "System." ) ) continue;

			var assemblyMetadata = a.GetCustomAttributes<System.Reflection.AssemblyMetadataAttribute>().ToArray();
			var metadataDict = assemblyMetadata.ToDictionary( x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase );

			var addonIdent = metadataDict.GetValueOrDefault( "Ident" );

			if ( addonIdent is null )
				continue;

			long version = 0;
			if ( Application.GamePackage is not null && Application.GamePackage.FullIdent == addonIdent )
			{
				version = Application.GamePackage.Revision?.VersionId ?? version;
			}

			var stack = new StackTrace( exception, true );

			var stackObj = stack.GetFrames().Select( x => new
			{
				Type = x.GetMethod()?.DeclaringType?.FullName,
				Method = x.GetMethod()?.Name,
				MethodFull = x.GetMethod()?.ToString(),
				Line = x.GetFileLineNumber(),
				Column = x.GetFileColumnNumber(),
				File = x.GetFileName()

			} ).ToArray();

			var er = new EventRecord( "package.error" );
			er.SetValue( "ident", addonIdent );
			er.SetValue( "type", exception.GetType().FullName );
			er.SetValue( "assembly", a.GetName().Name );
			er.SetValue( "message", exception.Message );
			er.SetValue( "stackone", string.Join( '\n', stackObj.Take( 3 ).Select( x => $"{x.Type}.{x.Method} [{x.File}:{x.Line}]" ) ) );
			er.SetValue( "game", addonIdent );
			er.SetValue( "gameversion", version );
			er.SetValue( "host", Application.IsDedicatedServer ? "dedicated" : "game" );
			er.SetValue( "editor", Application.IsEditor );
			er.SetValue( "netversion", Environment.Version.ToString() );
			er.SetValue( "os", Environment.OSVersion.ToString() );
			er.SetValue( "uptime", (DateTime.UtcNow - Process.GetCurrentProcess().StartTime.ToUniversalTime()).TotalSeconds );
			er.SetValue( "currentCulture", CultureInfo.CurrentCulture.Name );
			er.SetValue( "currentUICulture", CultureInfo.CurrentUICulture.Name );
			er.SetValue( "hardware", SystemInfo.AsObject() );
			er.SetValue( "commandLineArgs", string.Join( " ", Environment.GetCommandLineArgs() ) );
			er.SetValue( "assemblymeta", metadataDict );
			er.SetValue( "stack", stackObj );

			// In the editor we don't care about exceptions
			// because there are a wide range of things they could be 
			// fucking up.
			if ( !Application.IsEditor )
			{
				er.Submit();
			}

			return true;
		}

		return false;
	}
}
