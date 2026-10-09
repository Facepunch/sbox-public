using System.Diagnostics;
using Spectre.Console;
using Spectre.Console.Rendering;
using static Facepunch.Constants;

namespace Facepunch;

internal static class BuildDisplay
{
	private static readonly object sync = new();
	private static volatile Session current;

	private sealed class Session( string title, string[] stages )
	{
		public readonly object Sync = new();
		public readonly AutoResetEvent Wake = new( false );
		public readonly Queue<(string Message, string Style)> Messages = new();
		public readonly Stopwatch Timer = Stopwatch.StartNew();
		public readonly string Title = Log.TerminalText( title );
		public readonly string[] Stages = stages.Select( Log.TerminalText ).ToArray();
		public readonly string[] Results = stages.Select( _ => "Pending" ).ToArray();
		public readonly TimeSpan[] Started = new TimeSpan[stages.Length];
		public readonly TimeSpan[] Elapsed = new TimeSpan[stages.Length];
		public int Stage = -1;
		public string Activity = "Starting";
		public string Detail = "";
		public double Value;
		public double Maximum;
		public bool Finished;
	}

	public static bool IsActive => current != null;

	private static void WriteHeader( IAnsiConsole console, bool rich )
	{
		console.WriteLine();
		if ( !rich || console.Profile.Width < 40 )
		{
			console.Write( new Text( "s&box / bootstrap\n" ) );
			return;
		}

		const string blue = "#168BEB";
		var path = Markup.Escape( Log.TerminalText( Directory.GetCurrentDirectory() ) );
		console.MarkupLine( $" [on {blue}]        [/]   [bold]s&box[/]" );
		console.MarkupLine( $" [bold white on {blue}]   s&   [/]   Engine bootstrap" );
		console.MarkupLine( $" [on {blue}]        [/]   [grey]{path}[/]" );
		console.WriteLine();
	}

	public static ExitCode Run( string title, Func<ExitCode> action ) => Run( title, action, [] );

	public static ExitCode Run( string title, Func<ExitCode> action, string[] stages )
	{
		Session session;
		lock ( sync )
		{
			session = current == null ? new Session( title, stages ?? [] ) : null;
			if ( session != null ) current = session;
		}
		if ( session == null ) return action();

		// Only this calling thread renders. The action and its workers publish state and FIFO messages.
		var task = Task.Run( () =>
		{
			try { return action(); }
			catch ( Exception ex )
			{
				Log.Error( ex.ToString() );
				return ExitCode.Failure;
			}
		} );
		try
		{
			var interactive = !Console.IsOutputRedirected && !Console.IsErrorRedirected
				&& string.IsNullOrEmpty( Environment.GetEnvironmentVariable( "CI" ) ) && !Utility.IsCi()
				&& Environment.GetEnvironmentVariable( "TERM" ) != "dumb";
			var console = AnsiConsole.Create( new AnsiConsoleSettings
			{
				Ansi = interactive ? AnsiSupport.Detect : AnsiSupport.No,
				Interactive = interactive ? InteractionSupport.Detect : InteractionSupport.No,
				Out = new AnsiConsoleOutput( Console.Out )
			} );
			interactive &= console.Profile.Capabilities.Ansi && console.Profile.Capabilities.Interactive;
			if ( session.Stages.Length > 0 ) WriteHeader( console, interactive );

			void Pump( LiveDisplayContext context )
			{
				string previous = null;
				var lastUpdate = TimeSpan.FromSeconds( -10 );
				var lastRender = TimeSpan.FromSeconds( -1 );
				do
				{
					Drain( session, context == null ? null : console );
					if ( context != null && (session.Timer.Elapsed - lastRender >= TimeSpan.FromMilliseconds( 100 ) || task.IsCompleted) )
					{
						context.UpdateTarget( Render( session ) );
						context.Refresh();
						lastRender = session.Timer.Elapsed;
					}
					else if ( context == null )
					{
						string text;
						TimeSpan elapsed;
						lock ( session.Sync )
						{
							var progress = session.Maximum > 0 ? $" {session.Value / session.Maximum:P0}" : "";
							text = $"{session.Activity}{progress}";
							if ( session.Detail.Length > 0 ) text += $"\n{session.Detail}";
							elapsed = session.Timer.Elapsed;
						}
						var since = elapsed - lastUpdate;
						if ( (text != previous && since.TotalSeconds >= 1) || since.TotalSeconds >= 10 )
						{
							Log.WriteConsole( $"{session.Title}: {text} ({FormatTime( elapsed )})", "" );
							previous = text;
							lastUpdate = elapsed;
						}
					}
					if ( task.IsCompleted ) break;
					session.Wake.WaitOne( 100 );
				} while ( true );

				FinishStage( task.GetAwaiter().GetResult() );
				lock ( session.Sync )
				{
					session.Finished = true;
					session.Timer.Stop();
					for ( var i = 0; i < session.Stages.Length; i++ )
						if ( session.Results[i] == "Pending" ) session.Results[i] = "Not run";
				}
				while ( Drain( session, context == null ? null : console ) ) { }
				if ( context != null )
				{
					context.UpdateTarget( Render( session ) );
					context.Refresh();
				}
			}

			if ( interactive )
			{
				try { console.Live( Render( session ) ).AutoClear( false ).Start( Pump ); }
				catch ( Exception ex )
				{
					Log.Warning( $"Live display unavailable: {ex.Message}. Continuing with plain output." );
					interactive = false;
					Pump( null );
				}
			}
			else Pump( null );

			for ( var i = 0; i < session.Stages.Length; i++ )
			{
				var message = $"{session.Stages[i]}: {session.Results[i]} ({FormatTime( session.Elapsed[i] )})";
				if ( interactive ) Log.Record( message );
				else
				{
					Log.Record( message );
					Write( message, "", true );
				}
			}
			var result = task.GetAwaiter().GetResult();
			var summary = $"{session.Title}: {(result == ExitCode.Success ? "succeeded" : "failed")} in {FormatTime( session.Timer.Elapsed )}";
			Log.Record( summary );
			Write( summary, "", true );
			while ( Drain( session, interactive ? console : null ) ) { }
			return result;
		}
		catch ( Exception ex )
		{
			Log.Error( $"Build display failed: {ex.Message}" );
			task.GetAwaiter().GetResult();
			FinishStage( ExitCode.Failure );
			Write( $"{session.Title}: failed in {FormatTime( session.Timer.Elapsed )}", "red", true );
			while ( Drain( session, null ) ) { }
			return ExitCode.Failure;
		}
		finally
		{
			// Do not dispose the queue's wake event while a producer can still reach this session.
			lock ( session.Sync )
			{
				current = null;
				session.Finished = true;
				session.Wake.Dispose();
			}
			// Flush messages accepted between the final drain and detaching the session.
			while ( Drain( session, null ) ) { }
		}
	}

	public static void Status( string message )
	{
		var session = current;
		if ( session == null ) return;
		lock ( session.Sync )
		{
			if ( session.Finished ) return;
			session.Activity = Log.TerminalText( message );
			session.Detail = "";
			session.Value = session.Maximum = 0;
			session.Wake.Set();
		}
	}

	public static void Progress( double value, double maximum, string detail = null )
	{
		var session = current;
		if ( session == null ) return;
		lock ( session.Sync )
		{
			if ( session.Finished ) return;
			session.Maximum = double.IsFinite( maximum ) && maximum > 0 ? maximum : 0;
			session.Value = double.IsFinite( value ) ? Math.Clamp( value, 0, session.Maximum ) : 0;
			if ( detail != null ) session.Detail = Log.TerminalText( detail );
			session.Wake.Set();
		}
	}

	public static void Detail( string message )
	{
		var session = current;
		if ( session == null ) return;
		lock ( session.Sync )
		{
			if ( session.Finished ) return;
			session.Detail = Log.TerminalText( message );
			session.Wake.Set();
		}
	}

	public static void StartStage( int index )
	{
		var session = current;
		if ( session == null ) return;
		string name;
		lock ( session.Sync )
		{
			if ( session.Finished || index < 0 || index >= session.Stages.Length ) return;
			session.Stage = index;
			session.Results[index] = "Running";
			session.Started[index] = session.Timer.Elapsed;
			name = session.Stages[index];
			Status( name );
		}
		Log.SetStage( name );
		Log.Detail( $"Starting {name}" );
	}

	public static void FinishStage( ExitCode result )
	{
		var session = current;
		if ( session == null ) return;
		string message;
		lock ( session.Sync )
		{
			var index = session.Stage;
			if ( session.Finished || index < 0 || session.Results[index] != "Running" ) return;
			session.Elapsed[index] = session.Timer.Elapsed - session.Started[index];
			session.Results[index] = result == ExitCode.Success ? "Done" : "Failed";
			message = $"{session.Stages[index]}: {session.Results[index]} ({FormatTime( session.Elapsed[index] )})";
			session.Wake.Set();
		}
		Log.Detail( message );
	}

	public static bool Write( string message, string style, bool always = false )
	{
		var session = current;
		if ( session == null ) return false;
		var visible = always || Log.Verbose || !Log.IsBootstrapActive;
		lock ( session.Sync )
		{
			if ( current != session ) return false;
			if ( visible )
			{
				session.Messages.Enqueue( (Log.TerminalText( message ), style) );
				session.Wake.Set();
			}
			return true;
		}
	}

	private static bool Drain( Session session, IAnsiConsole console )
	{
		for ( var i = 0; i < 1000; i++ )
		{
			(string Message, string Style) message;
			lock ( session.Sync )
			{
				if ( !session.Messages.TryDequeue( out message ) ) return false;
			}
			if ( console == null ) Log.WriteConsole( message.Message, message.Style );
			else
			{
				// Ordinary messages and the final summary use the terminal's default style.
				// Spectre treats an empty style string as invalid rather than as a default.
				var style = string.IsNullOrWhiteSpace( message.Style ) ? Style.Plain : Style.Parse( message.Style );
				console.Write( new Text( message.Message + Environment.NewLine, style ) );
			}
		}
		return true;
	}

	private static string FormatTime( TimeSpan time ) => time.TotalHours >= 1
		? $"{(int)time.TotalHours}:{time.Minutes:00}:{time.Seconds:00}"
		: $"{(int)time.TotalMinutes:00}:{time.Seconds:00}";

	private static IRenderable Render( Session session )
	{
		lock ( session.Sync )
		{
			var table = new Table().Border( TableBorder.Simple ).Expand();
			table.AddColumn( Markup.Escape( session.Title ) );
			table.AddColumn( "Status" );
			table.AddColumn( "Elapsed" );
			for ( var i = 0; i < session.Stages.Length; i++ )
			{
				var elapsed = session.Results[i] == "Running" ? session.Timer.Elapsed - session.Started[i] : session.Elapsed[i];
				var color = session.Results[i] switch { "Done" => "green", "Failed" => "red", "Running" => "cyan", _ => "grey" };
				table.AddRow( Markup.Escape( session.Stages[i] ), $"[{color}]{session.Results[i]}[/]", FormatTime( elapsed ) );
			}
			var rows = new List<IRenderable>();
			if ( session.Stages.Length > 0 ) rows.Add( table );
			else rows.Add( new Text( session.Title, new Style( Color.Aqua ) ) );
			if ( !session.Finished )
			{
				rows.Add( new Text( $"{session.Activity} ({FormatTime( session.Timer.Elapsed )})", new Style( Color.Aqua ) ).Overflow( Overflow.Ellipsis ) );
				if ( session.Maximum > 0 )
				{
					var fraction = Math.Clamp( session.Value / session.Maximum, 0, 1 );
					var filled = (int)(fraction * 30);
					rows.Add( new Text( $"[{new string( '#', filled )}{new string( '-', 30 - filled )}] {fraction:P0}", new Style( Color.Aqua ) ) );
				}
				if ( session.Detail.Length > 0 ) rows.Add( new Text( session.Detail ) );
			}
			return new Rows( rows );
		}
	}
}
