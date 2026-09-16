using System.Text;

namespace Facepunch;

public static class Log
{
	private static readonly object sync = new();
	private static readonly object consoleSync = new();
	private static readonly Queue<string> tail = new();
	private static Dictionary<(string Stage, string Style, string Text), int> diagnostics;
	private static string stage;
	private static bool verbose;

	internal static bool IsBootstrapActive { get { lock ( sync ) return diagnostics != null; } }
	internal static bool Verbose { get { lock ( sync ) return verbose; } }

	public static void Info( string message )
	{
		if ( !IsBootstrapActive || Verbose ) BuildDisplay.Detail( message );
		Write( message, "" );
	}
	public static void Warning( string message ) => Write( message, "yellow", true );
	public static void Error( string message ) => Write( message, "red", true );
	public static void Header( string message )
	{
		BuildDisplay.Status( message );
		Write( $"\n========== {message} ========== ", "cyan" );
	}

	internal static void StartBootstrap( bool verbose )
	{
		lock ( sync )
		{
			diagnostics = new();
			stage = "Bootstrap";
			Log.verbose = verbose;
			tail.Clear();
		}
	}

	internal static void FinishBootstrap()
	{
		Dictionary<(string Stage, string Style, string Text), int> deferred;
		lock ( sync )
		{
			deferred = diagnostics;
			diagnostics = null;
			stage = null;
			verbose = false;
			tail.Clear();
		}

		// The caller tears down the display first; never enter it while holding sync.
		if ( deferred == null ) return;
		foreach ( var group in deferred.GroupBy( entry => entry.Key.Stage ) )
		{
			WriteConsole( $"\n{group.Key}:", "cyan" );
			foreach ( var entry in group )
				WriteConsole( entry.Key.Text + (entry.Value > 1 ? $" (repeated {entry.Value} times)" : ""), entry.Key.Style );
		}
	}

	internal static void SetStage( string stage )
	{
		lock ( sync ) Log.stage = string.IsNullOrWhiteSpace( stage ) ? "Bootstrap" : TerminalText( stage );
	}

	internal static void Record( string message )
	{
		lock ( sync )
		{
			foreach ( var line in TerminalText( message ).Split( '\n' ) )
			{
				tail.Enqueue( line );
				while ( tail.Count > 25 ) tail.Dequeue();
			}
		}
	}

	private static void Defer( string message, string style )
	{
		var key = (stage, style, TerminalText( message ));
		diagnostics.TryGetValue( key, out var count );
		diagnostics[key] = count + 1;
	}

	internal static void Detail( string message ) => Write( message, "dim" );
	internal static void Summary( string message ) => Write( message, "", true );
	internal static void ClearTail() { lock ( sync ) tail.Clear(); }

	internal static void CaptureFailure()
	{
		lock ( sync )
		{
			if ( diagnostics == null || verbose || tail.Count == 0 ) return;
			// A failed subprocess already supplied its complete output and command.
			if ( diagnostics.Keys.Any( key => key.Stage == stage && key.Style == "red" ) ) return;
			Defer( "Recent output:\n" + string.Join( '\n', tail ), "red" );
		}
	}

	internal static void ProcessOutput( string message, bool error = false )
	{
		// Output text and stream choice aren't a success/failure protocol. RunProcess
		// reports the buffered output only when the process exits unsuccessfully.
		Detail( message );
	}

	private static void Write( string message, string style, bool always = false )
	{
		lock ( sync )
		{
			Record( message );
			if ( diagnostics != null && !verbose )
			{
				if ( style is "yellow" or "red" ) Defer( message, style );
				return;
			}
		}
		if ( !BuildDisplay.Write( message, style, always ) ) WriteConsole( message, style );
	}

	internal static void WriteConsole( string message, string style )
	{
		lock ( consoleSync )
		{
			try
			{
				if ( Console.IsOutputRedirected || !string.IsNullOrEmpty( Environment.GetEnvironmentVariable( "CI" ) ) || Utility.IsCi() )
				{
					Console.WriteLine( TerminalText( message ) );
					return;
				}
				var previous = Console.ForegroundColor;
				try
				{
					Console.ForegroundColor = style switch { "yellow" => ConsoleColor.Yellow, "red" => ConsoleColor.Red, "cyan" => ConsoleColor.Cyan, _ => previous };
					Console.WriteLine( TerminalText( message ) );
				}
				finally { Console.ForegroundColor = previous; }
			}
			catch ( IOException ) { }
			catch ( ObjectDisposedException ) { }
		}
	}

	internal static string TerminalText( string message )
	{
		if ( string.IsNullOrEmpty( message ) ) return "";
		var text = new StringBuilder( message.Length );
		for ( var i = 0; i < message.Length; i++ )
		{
			var ch = message[i];
			if ( ch == '\x1b' || ch is '\u009b' or '\u009d' or '\u0090' or '\u0098' or '\u009e' or '\u009f' )
			{
				if ( ch == '\x1b' && ++i == message.Length ) break;
				var code = message[i];
				if ( code is '[' or '\u009b' )
				{
					while ( ++i < message.Length && !(message[i] >= '@' && message[i] <= '~') ) { }
				}
				else if ( code is ']' or 'P' or 'X' or '^' or '_' or '\u009d' or '\u0090' or '\u0098' or '\u009e' or '\u009f' )
				{
					while ( ++i < message.Length )
					{
						if ( message[i] is '\a' or '\u009c' ) break;
						if ( message[i] == '\x1b' && i + 1 < message.Length && message[i + 1] == '\\' ) { i++; break; }
					}
				}
				else if ( code >= ' ' && code <= '/' )
				{
					while ( i + 1 < message.Length && message[i] >= ' ' && message[i] <= '/' ) i++;
				}
				continue;
			}
			if ( ch == '\r' )
			{
				if ( i + 1 >= message.Length || message[i + 1] != '\n' ) text.Append( '\n' );
			}
			else if ( ch == '\t' ) text.Append( "    " );
			else if ( ch == '\n' || (!char.IsControl( ch ) && char.GetUnicodeCategory( ch ) != System.Globalization.UnicodeCategory.Format) ) text.Append( ch );
		}
		return text.ToString();
	}
}
