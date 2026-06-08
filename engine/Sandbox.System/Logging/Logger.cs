using NLog;
using System.Web;

namespace Sandbox.Diagnostics;

[StackTraceHidden]
public class Logger
{
	private NLog.Logger _log;

	/// <summary>
	/// Name of this logger.
	/// </summary>
	public string Name { get; protected set; }

	public Logger( string name )
	{
		Logging.InitializeConfig();

		Name = name;
		_log = NLog.LogManager.GetLogger( name );

		Logging.RegisterLogger( Name );
	}

	internal void DoInfo( FormattableString message ) => WriteToTargets( NLog.LogLevel.Info, null, message );
	internal void DoTrace( FormattableString message ) => WriteToTargets( NLog.LogLevel.Trace, null, message );
	internal void DoWarning( FormattableString message ) => WriteToTargets( NLog.LogLevel.Warn, null, message );
	internal void DoError( FormattableString message ) => WriteToTargets( NLog.LogLevel.Error, null, message );

	/// <inheritdoc cref="Info(object)"/>
	public void Info( FormattableString message ) => DoInfo( message );
	/// <inheritdoc cref="Trace(object)"/>
	public void Trace( FormattableString message ) => DoTrace( message );
	/// <inheritdoc cref="Warning(object)"/>
	public void Warning( FormattableString message ) => DoWarning( message );
	/// <inheritdoc cref="Error(object)"/>
	public void Error( FormattableString message ) => DoError( message );

	/// <inheritdoc cref="Error(Exception, object)"/>
	public void Error( Exception exception, FormattableString message ) => WriteToTargets( NLog.LogLevel.Error, exception, message );

	/// <summary>
	/// Log an exception as an error, with given message override.
	/// </summary>
	/// <param name="exception">The exception to log.</param>
	/// <param name="message">The text to override exceptions' message with in the log.</param>
	public void Error( Exception exception, object message ) => Error( exception, $"{message}" );

	/// <summary>
	/// Log an exception as an error.
	/// </summary>
	/// <param name="exception">The exception to log.</param>
	public void Error( Exception exception ) => WriteToTargets( NLog.LogLevel.Error, exception, $"{exception.Message}" );

	/// <inheritdoc cref="Warning(Exception, object)"/>
	public void Warning( Exception exception, FormattableString message ) => WriteToTargets( NLog.LogLevel.Warn, exception, message );

	/// <summary>
	/// Log an exception as a warning, with given message override.
	/// </summary>
	/// <param name="exception">The exception to log.</param>
	/// <param name="message">The text to override exceptions' message with in the log.</param>
	public void Warning( Exception exception, object message ) => Warning( exception, $"{message}" );

	/// <summary>
	/// Log some information. This is the default log severity level.
	/// </summary>
	/// <param name="message">The information to log.</param>
	public void Info( object message )
	{
		if ( message is Exception ex )
		{
			WriteToTargets( NLog.LogLevel.Info, ex, $"{ex.Message}" );
			return;
		}

		Info( $"{message}" );
	}

	/// <summary>
	/// Log some information. This is least severe log level.
	/// </summary>
	/// <param name="message">The information to log.</param>
	public void Trace( object message )
	{
		if ( message is Exception ex )
		{
			WriteToTargets( NLog.LogLevel.Trace, ex, $"{ex.Message}" );
			return;
		}

		Trace( $"{message}" );
	}

	/// <summary>
	/// Log a warning. This is the second most severe log level.
	/// </summary>
	/// <param name="message">The warning to log.</param>
	public void Warning( object message )
	{
		if ( message is Exception ex )
		{
			Warning( ex, ex.Message );
			return;
		}

		Warning( $"{message}" );
	}

	/// <summary>
	/// Log an error. This is the most severe log level.
	/// </summary>
	/// <param name="message">The error to log.</param>
	public void Error( object message )
	{
		if ( message is Exception ex )
		{
			Error( ex, ex.Message );
			return;
		}

		Error( $"{message}" );
	}

	internal void WriteToTargets( NLog.LogLevel nlogLevel, Exception ex, FormattableString message, string name = null )
	{
		name ??= Name;

		LogLevel level = nlogLevel.Ordinal switch
		{
			0 => LogLevel.Trace,
			1 => LogLevel.Trace,
			2 => LogLevel.Info,
			3 => LogLevel.Warn,
			4 => LogLevel.Error,
			5 => LogLevel.Error,
			_ => LogLevel.Info,
		};

		if ( !Logging.ShouldLog( name, level ) )
			return;

		var defaultMessage = message.ToString();
		var arguments = new List<object>();

		var htmlMessage = (string)WrapObject( message, arguments );

		// Only show the first line in the console, but inspecting that line will show the rest

		var firstLineBreakIndex = htmlMessage.IndexOf( "\n", StringComparison.Ordinal );
		if ( firstLineBreakIndex > -1 )
		{
			htmlMessage = htmlMessage.Substring( 0, firstLineBreakIndex ).TrimEnd();
		}

		var logEvent = LogEventInfo.Create( nlogLevel, name, defaultMessage );

		if ( ex != null )
		{
			logEvent.Exception = ex;
		}
		else
		{
			var stackTrace = new StackTrace( 0, true );
			logEvent.SetStackTrace( stackTrace, 0 );
		}

		_log.Log( logEvent );

		string stacktrace = null;

		if ( logEvent.Exception != null )
		{
			stacktrace = GameLog.WriteExceptionDetails( logEvent.Exception );
		}

		if ( stacktrace == null && logEvent.StackTrace != null )
		{
			stacktrace = logEvent.StackTrace.ToString();
		}

		var e = new LogEvent
		{
			Level = level,
			Logger = logEvent.LoggerName,
			Message = defaultMessage,
			Exception = ex,
			HtmlMessage = htmlMessage,
			Stack = stacktrace,
			Time = DateTime.Now,
			Arguments = arguments.ToArray()
		};

		Logging.Write( e );
	}

	/// <summary>
	/// Wrap / escape an object for html log messages. Inspectable objects
	/// will be wrapped in a link, and added to <paramref name="outArgs"/>.
	/// The link will index into <paramref name="outArgs"/>. <see cref="FormattableString"/>s
	/// will recurse into <see cref="WrapObject"/>, so their arguments can also be inspected.
	/// </summary>
	/// <param name="o">Object to wrap</param>
	/// <param name="outArgs">Inspectable objects will be added here</param>
	/// <returns>Html-wrapped object. Either a string or a primitive.</returns>
	static object WrapObject( object o, List<object> outArgs )
	{
		if ( o == null ) return "null";

		if ( o is FormattableString formattable )
		{
			var wrappedArgs = formattable.GetArguments()
				.Select( x => WrapObject( x, outArgs ) )
				.ToArray();

			return string.Format( formattable.Format, wrappedArgs );
		}

		if ( o is string ) return HttpUtility.HtmlEncode( o );

		var t = o.GetType();
		if ( t.IsPrimitive ) return o;

		var index = outArgs.Count;
		outArgs.Add( o );

		return $"<a href=\"arg:{index}\" style=\"\">{HttpUtility.HtmlEncode( o.ToString() )}</a>";
	}

}
