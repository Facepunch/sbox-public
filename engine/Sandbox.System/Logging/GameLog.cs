using NLog;
using NLog.Targets;

namespace Sandbox
{
	[Target( "GameLog" )]
	internal sealed class GameLog : Target
	{
		private readonly object syncRoot = new object();

		protected override void Write( LogEventInfo logEvent )
		{
			lock ( syncRoot )
			{
				ProtectedWrite( logEvent );
			}
		}

		void ProtectedWrite( LogEventInfo logEvent )
		{
			string stacktrace = null;

			if ( logEvent.Exception != null )
			{
				stacktrace = WriteExceptionDetails( logEvent.Exception );
			}

			if ( stacktrace == null && logEvent.StackTrace != null )
			{
				stacktrace = logEvent.StackTrace.ToString();
			}

			var l = Sandbox.LogLevel.Trace;
			if ( logEvent.Level == NLog.LogLevel.Info ) l = LogLevel.Info;
			if ( logEvent.Level == NLog.LogLevel.Warn ) l = LogLevel.Warn;
			if ( logEvent.Level == NLog.LogLevel.Error ) l = LogLevel.Error;

			var e = new LogEvent
			{
				Level = l,
				Logger = logEvent.LoggerName,
				Message = logEvent.FormattedMessage,
				Stack = stacktrace,
				Time = DateTime.Now
			};

			if ( logEvent.Exception != null && logEvent.Level >= NLog.LogLevel.Error )
			{
				Diagnostics.Logging.OnException?.Invoke( logEvent.Exception );
			}

			Diagnostics.Logging.Capture?.Invoke()?.AppendLine( logEvent.FormattedMessage );

			if ( Diagnostics.Logging.PrintToConsole )
			{
				var now = DateTime.Now;
				var stream = e.Level == LogLevel.Error ? Console.Error : Console.Out;

				Console.ForegroundColor = ConsoleColor.Cyan;
				stream.Write( now.ToString( "hh:mm:ss " ) );

				var logger = e.Logger ?? " ";
				if ( logger.Length > 8 ) logger = logger.Substring( 0, 8 );
				if ( logger.Length < 8 ) logger += new string( ' ', 8 - logger.Length );

				Console.ForegroundColor = ConsoleColor.Green;
				stream.Write( logger );

				Console.ForegroundColor = GetConsoleColor( e );

				var lines = logEvent.FormattedMessage.Split( '\n', '\r' );

				stream.WriteLine( $" {lines[0]}" );

				for ( int i = 1; i < lines.Length; i++ )
				{
					stream.WriteLine( $"                 {lines[i]}" );
				}
			}
		}

		private ConsoleColor GetConsoleColor( LogEvent e )
		{
			if ( e.Level == LogLevel.Trace ) return ConsoleColor.DarkGray;
			if ( e.Level == LogLevel.Error ) return ConsoleColor.Red;
			if ( e.Level == LogLevel.Warn ) return ConsoleColor.Yellow;

			return ConsoleColor.White;
		}

		internal static string WriteExceptionDetails( Exception exception )
		{
			if ( exception == null )
				return null;

			var sb = new StringBuilder();
			UnwrapException( exception, sb );
			return sb.ToString();
		}

		private static void UnwrapException( Exception exception, StringBuilder sb )
		{
			var baseException = exception.GetBaseException();

			if ( baseException is AggregateException aggregate )
			{
				var innerExceptions = aggregate.InnerExceptions;

				if ( innerExceptions.Count == 0 )
				{
					PrintException( aggregate, sb );
					return;
				}

				if ( innerExceptions.Count == 1 )
				{
					UnwrapException( innerExceptions[0], sb );
					return;
				}

				sb.AppendLine( $"Aggregate of {innerExceptions.Count} exceptions:" );

				for ( var i = 0; i < innerExceptions.Count; i++ )
				{
					sb.AppendLine( $">>> Exception {i}:" );
					UnwrapException( innerExceptions[i], sb );
				}

				return;
			}

			PrintException( baseException, sb );
		}

		private static void PrintException( Exception exception, StringBuilder sb )
		{
			sb.AppendLine( exception?.ToString() ?? "<null>" );
		}
	}
}
